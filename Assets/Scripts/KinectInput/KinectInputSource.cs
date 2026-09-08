using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Reçoit les commandes de vol émises par le bridge Python (Kinect) en UDP et les expose
/// sous la même forme qu'un input clavier : un Vector3 (x = gauche/droite, y = altitude,
/// z = vitesse), directement consommable par MoveBird.
///
/// Aucune scène à modifier : si personne n'a posé le composant à la main, il est créé
/// automatiquement au lancement (voir AutoCreate) et exposé via <see cref="Instance"/>.
/// MoveBird le consulte par ce biais.
///
/// Le clavier reste toujours fonctionnel : bridge coupé ou aucun joueur détecté,
/// HasPlayer est false et le jeu retombe seul sur les touches.
/// </summary>
public class KinectInputSource : MonoBehaviour
{
    /// <summary>
    /// Écouteur actif du jeu. Renseigné par le composant posé dans la scène s'il y en a un,
    /// sinon par le bootstrap automatique. Null tant qu'aucune scène n'a fini de charger.
    /// </summary>
    public static KinectInputSource Instance { get; private set; }

    /// <summary>
    /// Mettre à false (depuis un RuntimeInitializeOnLoadMethod BeforeSceneLoad) pour empêcher
    /// la création automatique et gérer l'écouteur soi-même.
    /// </summary>
    public static bool AutoBootstrap = true;

    [Header("Réseau")]
    [Tooltip("Doit correspondre au --port du bridge Python (défaut 7777).")]
    public int port = 7777;

    [Header("Fraîcheur des données")]
    [Tooltip("Au-delà de ce délai sans paquet, on considère qu'il n'y a plus de joueur.")]
    public float playerTimeoutSeconds = 0.25f;

    [Tooltip("Au-delà de ce délai sans AUCUN paquet, le bridge Python est considéré mort.")]
    public float bridgeTimeoutSeconds = 1.0f;

    // --- Contrat réseau : doit rester synchronisé avec tools/kinect_bridge/kibird_bridge/protocol.py ---
    private const int HeaderSize = 43;
    private const int JointCount = 9;
    private const int JointSize = 16;
    private const int PacketSize = HeaderSize + JointCount * JointSize; // 187
    private const byte ProtocolVersion = 1;
    private const byte FlagPlayerPresent = 1 << 0;
    private const byte FlagInZone = 1 << 1;
    private const byte FlagReplayMode = 1 << 2;

    /// <summary>Noms des articulations, dans l'ordre du protocole (debug/affichage uniquement).</summary>
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
        public bool ReplayMode;
        public float Distance;
        public float Lean, Lift, Throttle, Glide;
        public float Confidence;
    }

    // --- État partagé entre le thread réseau et le thread principal ---
    // Le thread réseau n'appelle JAMAIS d'API Unity et ne lève aucun event : il se contente
    // d'écrire ici. Tout le reste est lu depuis Update(), sur le thread principal.
    /// <summary>Écart de séquence au-delà duquel on considère que le bridge Python a redémarré.</summary>
    private const long BridgeRestartSeqGap = 100;

    private readonly object _lock = new object();
    private Snapshot _pending;
    private readonly Joint[] _pendingJoints = new Joint[JointCount];
    private bool _hasPending;
    private long _lastSeq = -1;
    private double _lastPacketRealtime = double.NegativeInfinity;

    // --- État lu par le jeu (thread principal uniquement) ---
    private Snapshot _current;
    private readonly Joint[] _currentJoints = new Joint[JointCount];
    private float _lastPacketTime = float.NegativeInfinity;
    private float _latencyMs;

    private Socket _socket;
    private Thread _thread;
    private volatile bool _running;

    /// <summary>Un joueur est détecté, dans la zone, et les données sont fraîches.</summary>
    public bool HasPlayer =>
        _current.PlayerPresent && _current.InZone &&
        (Time.realtimeSinceStartup - _lastPacketTime) < playerTimeoutSeconds;

    /// <summary>Le bridge Python émet toujours (heartbeat), même sans joueur devant la Kinect.</summary>
    public bool BridgeAlive =>
        (Time.realtimeSinceStartup - _lastPacketTime) < bridgeTimeoutSeconds;

    /// <summary>Commandes de vol, au format attendu par MoveBird.GetInput().</summary>
    public Vector3 Input => new Vector3(_current.Lean, _current.Lift, _current.Throttle);

    public float Distance => _current.Distance;
    public float Glide => _current.Glide;
    public float Confidence => _current.Confidence;
    public bool InZone => _current.InZone;
    public bool ReplayMode => _current.ReplayMode;
    public uint Sequence => _current.Seq;

    /// <summary>Latence bout-en-bout mesurée (capture Kinect -> réception Unity), en ms.</summary>
    public float LatencyMs => _latencyMs;

    /// <summary>Articulations brutes, pour l'overlay de debug. Ne pas modifier le contenu.</summary>
    public Joint[] Joints => _currentJoints;

    /// <summary>
    /// Crée l'écouteur si la scène chargée n'en contient pas. Volontairement AfterSceneLoad :
    /// un composant posé à la main dans la scène (avec ses réglages) doit gagner sur celui-ci.
    /// Ne tourne qu'une fois par lancement, et l'objet survit aux rechargements de scène —
    /// le port UDP n'est donc pas relié à chaque redémarrage de partie.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!AutoBootstrap || Instance != null) return;

        var go = new GameObject("KinectInput (auto)");
        go.AddComponent<KinectInputSource>();
        // Overlay masqué par défaut (on est en configuration JPO), F1 pour l'afficher.
        go.AddComponent<KinectDebugOverlay>().visible = false;
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Deux écouteurs relieraient le même port : le second volerait une partie des
            // paquets au premier, ce qui se traduirait par des saccades difficiles à diagnostiquer.
            Debug.LogWarning($"[KinectInput] Un KinectInputSource existe déjà : '{name}' est " +
                             "désactivé pour ne pas ouvrir le port UDP deux fois.");
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
        // Sans cette fermeture, le port reste pris entre deux Play dans l'Éditeur.
        StopReceiver();
    }

    private void StartReceiver()
    {
        if (_running) return;

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.ReceiveTimeout = 500; // permet au thread de vérifier _running régulièrement
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        }
        catch (SocketException e)
        {
            Debug.LogError($"[KinectInput] Impossible d'ouvrir le port UDP {port} : {e.Message}. " +
                           "Un autre process l'utilise-t-il (un monitor Python, une session Unity restée ouverte) ?");
            return;
        }

        lock (_lock)
        {
            // Repartir propre à chaque Play : sinon le seq d'une session précédente ferait
            // rejeter les premiers paquets de la nouvelle.
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
            try { _socket.Close(); } catch { /* socket déjà fermée */ }
            _socket = null;
        }
        if (_thread != null)
        {
            _thread.Join(1000);
            _thread = null;
        }
    }

    /// <summary>
    /// Boucle du thread réseau. Elle consomme les paquets aussi vite qu'ils arrivent et ne
    /// conserve que le plus récent : sans ça, dès que Unity descend sous 30 FPS, une file
    /// s'accumulerait et ajouterait une latence qui ne redescendrait jamais.
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
                continue; // timeout de réception, ou socket fermée pendant l'arrêt
            }
            catch (ObjectDisposedException)
            {
                return; // arrêt en cours
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
                // Les paquets UDP peuvent arriver dans le désordre : on ignore tout retardataire.
                // Mais un seq très inférieur au dernier reçu signifie que le bridge Python a
                // redémarré (son compteur repart à 0) : il faut alors repartir de zéro, sinon
                // Unity rejetterait définitivement tous les paquets du nouveau bridge.
                bool bridgeRestarted = seq + BridgeRestartSeqGap < _lastSeq;
                if (!bridgeRestarted && seq <= _lastSeq) continue;
                _lastSeq = seq;

                _pending.Seq = seq;
                _pending.Timestamp = BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(span.Slice(10, 8)));
                _pending.PlayerPresent = (flags & FlagPlayerPresent) != 0;
                _pending.InZone = (flags & FlagInZone) != 0;
                _pending.ReplayMode = (flags & FlagReplayMode) != 0;
                _pending.Distance = ReadFloat(span, 18);
                _pending.Lean = ReadFloat(span, 22);
                _pending.Lift = ReadFloat(span, 26);
                _pending.Throttle = ReadFloat(span, 30);
                _pending.Glide = ReadFloat(span, 34);
                _pending.Confidence = ReadFloat(span, 38);

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
