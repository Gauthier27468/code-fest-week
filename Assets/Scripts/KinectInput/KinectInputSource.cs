using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Recoit les commandes de vol du bridge Python (Kinect) en UDP et les expose comme un input
/// clavier : un Vector3 (x = gauche/droite, y = altitude, z = vitesse) consommable par MoveBird.
/// Se cree tout seul au lancement si la scene n'en contient pas. Bridge coupe ou aucun joueur
/// detecte : HasPlayer est false et le jeu retombe sur le clavier.
/// </summary>
public class KinectInputSource : MonoBehaviour
{
    public static KinectInputSource Instance { get; private set; }

    [Header("Reseau")]
    [Tooltip("Doit correspondre au --port du bridge Python (defaut 7777).")]
    public int port = 7777;

    [Header("Fraicheur des donnees")]
    [Tooltip("Au-dela de ce delai sans paquet, on considere qu'il n'y a plus de joueur.")]
    public float playerTimeoutSeconds = 0.25f;

    [Tooltip("Au-dela de ce delai sans AUCUN paquet, le bridge Python est considere mort.")]
    public float bridgeTimeoutSeconds = 1.0f;

    // Contrat reseau : doit rester synchronise avec tools/kinect_bridge/kibird_bridge/protocol.py
    private const int HeaderSize = 43;
    private const int JointCount = 9;
    private const int JointSize = 16;
    private const int PacketSize = HeaderSize + JointCount * JointSize;
    private const byte ProtocolVersion = 1;
    private const byte FlagPlayerPresent = 1 << 0;
    private const byte FlagInZone = 1 << 1;

    /// <summary>Noms des articulations, dans l'ordre du protocole (affichage de debug).</summary>
    public static readonly string[] JointNames =
    {
        "NOSE", "L_SHOULDER", "R_SHOULDER", "L_ELBOW", "R_ELBOW",
        "L_WRIST", "R_WRIST", "L_HIP", "R_HIP"
    };

    public struct Joint
    {
        public float X, Y, Z, Confidence;
    }

    private struct Snapshot
    {
        public uint Seq;
        public double Timestamp;
        public bool PlayerPresent;
        public bool InZone;
        public float Distance;
        public float Lean, Lift, Throttle, Glide;
    }

    /// <summary>Ecart de sequence au-dela duquel on considere que le bridge Python a redemarre.</summary>
    private const long BridgeRestartSeqGap = 100;

    // Etat partage : le thread reseau n'appelle aucune API Unity, il ecrit seulement ici.
    private readonly object _lock = new object();
    private Snapshot _pending;
    private readonly Joint[] _pendingJoints = new Joint[JointCount];
    private bool _hasPending;
    private long _lastSeq = -1;
    private double _lastPacketRealtime = double.NegativeInfinity;

    // Etat lu par le jeu, thread principal uniquement.
    private Snapshot _current;
    private readonly Joint[] _currentJoints = new Joint[JointCount];
    private float _lastPacketTime = float.NegativeInfinity;
    private float _latencyMs;

    private Socket _socket;
    private Thread _thread;
    private volatile bool _running;

    /// <summary>Un joueur est detecte, dans la zone, et les donnees sont fraiches.</summary>
    public bool HasPlayer =>
        _current.PlayerPresent && _current.InZone &&
        (Time.realtimeSinceStartup - _lastPacketTime) < playerTimeoutSeconds;

    /// <summary>Le bridge Python emet toujours, meme sans joueur devant la Kinect.</summary>
    public bool BridgeAlive =>
        (Time.realtimeSinceStartup - _lastPacketTime) < bridgeTimeoutSeconds;

    /// <summary>Commandes de vol, au format attendu par MoveBird.GetInput().</summary>
    public Vector3 Input => new Vector3(_current.Lean, _current.Lift, _current.Throttle);

    public float Distance => _current.Distance;
    public float Glide => _current.Glide;
    public bool InZone => _current.InZone;
    public uint Sequence => _current.Seq;

    /// <summary>Latence bout-en-bout mesuree (capture Kinect -> reception Unity), en ms.</summary>
    public float LatencyMs => _latencyMs;

    /// <summary>Articulations brutes, pour l'overlay de debug. Ne pas modifier le contenu.</summary>
    public Joint[] Joints => _currentJoints;

    /// <summary>
    /// Cree l'ecouteur si la scene chargee n'en contient pas. AfterSceneLoad pour qu'un composant
    /// pose a la main (avec ses reglages) gagne sur celui-ci. L'objet survit aux rechargements de
    /// scene : le port UDP n'est pas relie a chaque redemarrage de partie.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;

        var go = new GameObject("KinectInput (auto)");
        go.AddComponent<KinectInputSource>();
        go.AddComponent<KinectDebugOverlay>().visible = false;
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Deux ecouteurs relieraient le meme port : le second volerait une partie des paquets.
            Debug.LogWarning($"[KinectInput] Un KinectInputSource existe deja : '{name}' est " +
                             "desactive pour ne pas ouvrir le port UDP deux fois.");
            enabled = false;
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        StartReceiver();
    }

    private void OnDisable()
    {
        StopReceiver();
    }

    private void OnApplicationQuit()
    {
        // Sans cette fermeture, le port reste pris entre deux Play dans l'Editeur.
        StopReceiver();
    }

    private void StartReceiver()
    {
        if (_running) return;

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.ReceiveTimeout = 500; // permet au thread de verifier _running regulierement
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        }
        catch (SocketException e)
        {
            Debug.LogError($"[KinectInput] Impossible d'ouvrir le port UDP {port} : {e.Message}. " +
                           "Un autre process l'utilise-t-il ?");
            return;
        }

        lock (_lock)
        {
            _lastSeq = -1;
            _hasPending = false;
        }

        _running = true;
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "KinectUdpReceiver" };
        _thread.Start();
    }

    private void StopReceiver()
    {
        _running = false;
        if (_socket != null)
        {
            try { _socket.Close(); } catch { }
            _socket = null;
        }
        if (_thread != null)
        {
            _thread.Join(1000);
            _thread = null;
        }
    }

    /// <summary>
    /// Consomme les paquets aussi vite qu'ils arrivent et ne conserve que le plus recent : sans
    /// ca, des que Unity descend sous 30 FPS une file s'accumule et la latence ne redescend plus.
    /// </summary>
    private void ReceiveLoop()
    {
        var buffer = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (_running)
        {
            int received;
            try
            {
                received = _socket.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException)
            {
                continue; // timeout de reception, ou socket fermee pendant l'arret
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (received < PacketSize) continue;
            if (buffer[0] != (byte)'K' || buffer[1] != (byte)'B' ||
                buffer[2] != (byte)'R' || buffer[3] != (byte)'D') continue;
            if (buffer[4] != ProtocolVersion) continue;

            var span = new ReadOnlySpan<byte>(buffer, 0, received);
            byte flags = buffer[5];
            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(6, 4));

            lock (_lock)
            {
                // Un seq tres inferieur au dernier recu signifie que le bridge a redemarre
                // (compteur reparti a 0) : sans ce cas, Unity rejetterait tous ses paquets.
                bool bridgeRestarted = seq + BridgeRestartSeqGap < _lastSeq;
                if (!bridgeRestarted && seq <= _lastSeq) continue;
                _lastSeq = seq;

                _pending.Seq = seq;
                _pending.Timestamp = BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(span.Slice(10, 8)));
                _pending.PlayerPresent = (flags & FlagPlayerPresent) != 0;
                _pending.InZone = (flags & FlagInZone) != 0;
                _pending.Distance = ReadFloat(span, 18);
                _pending.Lean = ReadFloat(span, 22);
                _pending.Lift = ReadFloat(span, 26);
                _pending.Throttle = ReadFloat(span, 30);
                _pending.Glide = ReadFloat(span, 34);
                // Octets 38-41 : confiance moyenne du squelette, non utilisee cote Unity.

                int count = Mathf.Min(buffer[42], JointCount);
                for (int i = 0; i < count; i++)
                {
                    int o = HeaderSize + i * JointSize;
                    _pendingJoints[i].X = ReadFloat(span, o);
                    _pendingJoints[i].Y = ReadFloat(span, o + 4);
                    _pendingJoints[i].Z = ReadFloat(span, o + 8);
                    _pendingJoints[i].Confidence = ReadFloat(span, o + 12);
                }

                _hasPending = true;
                _lastPacketRealtime = NowUnixSeconds();
            }
        }
    }

    private static float ReadFloat(ReadOnlySpan<byte> span, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4)));

    private static double NowUnixSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private void Update()
    {
        lock (_lock)
        {
            if (!_hasPending) return;

            _current = _pending;
            Array.Copy(_pendingJoints, _currentJoints, JointCount);
            _hasPending = false;
            _latencyMs = (float)((_lastPacketRealtime - _current.Timestamp) * 1000.0);
        }

        _lastPacketTime = Time.realtimeSinceStartup;
    }
}
