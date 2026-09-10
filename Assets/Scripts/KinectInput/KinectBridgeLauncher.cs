using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Lance et supervise le bridge Kinect en même temps que le jeu : un seul exécutable à démarrer.
/// Cherche d'abord le binaire gelé des StreamingAssets (machine de démo, sans Python ni uv),
/// sinon tools/kinect_bridge/run_bridge.sh (poste de dev). Le process lancé EST le bridge
/// (run_bridge.sh finit par un `exec`) : le tuer ici tue Python sans laisser de zombie sur la
/// Kinect ou le port UDP. Se crée tout seul au lancement, se désactive si rien n'est trouvé.
/// </summary>
public class KinectBridgeLauncher : MonoBehaviour
{
    private const string ScriptRelativePath = "tools/kinect_bridge/run_bridge.sh";
    private const string BundledBridgeRelativePath = "kinect_bridge/kibird_bridge";
    private const string ConfigFileName = "kibird-config.toml";

    [Tooltip("Nombre de relances automatiques tolérées avant d'abandonner.")]
    public int maxRestartAttempts = 5;

    [Tooltip("Délai avant de relancer le bridge après un arrêt inattendu.")]
    public float restartDelaySeconds = 2f;

    [Tooltip("Durée de fonctionnement continu au-delà de laquelle on considère le bridge " +
             "stable et on réarme le compteur de tentatives (sinon des coupures isolées et " +
             "espacées sur une journée de JPO finiraient par épuiser les relances pour de bon).")]
    public float stableUptimeSeconds = 15f;

    public static KinectBridgeLauncher Instance { get; private set; }

    private Process _process;
    private string _executable;   // programme à lancer (/bin/bash, ou le binaire gelé)
    private string _arguments;    // vide pour le binaire gelé
    private string _workingDir;
    private int _restartAttempts;
    private float _launchTime;
    private bool _quitting;
    private bool _restartQueued;
    private float _restartAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("KinectBridgeLauncher (auto)");
        go.AddComponent<KinectBridgeLauncher>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Le bridge est un binaire Linux natif : rien à lancer sur une autre plateforme.
        if (Application.platform != RuntimePlatform.LinuxPlayer &&
            Application.platform != RuntimePlatform.LinuxEditor)
        {
            enabled = false;
            return;
        }

        if (!ResolveBridgeCommand())
        {
            Debug.Log("[KinectBridge] Aucun bridge trouvé (ni binaire embarqué dans les " +
                      $"StreamingAssets, ni '{ScriptRelativePath}' à côté du jeu) : il ne sera pas " +
                      "lancé automatiquement, le clavier reste disponible comme d'habitude.");
            enabled = false;
            return;
        }

        LaunchProcess();
    }

    /// <summary>Binaire gelé en priorité, script source en repli. False si rien d'utilisable.</summary>
    private bool ResolveBridgeCommand()
    {
        // En build comme dans l'editeur, le fichier reste visible a la racine, a cote du jeu
        // ou du dossier Assets. Le bridge le cree lui-meme s'il a ete supprime.
        string appRoot = Directory.GetParent(Application.dataPath)?.FullName;
        string configPath = Path.Combine(appRoot ?? Application.dataPath, ConfigFileName);
        string configArguments = $"--config \"{configPath}\"";

        string bundled = Path.Combine(Application.streamingAssetsPath, BundledBridgeRelativePath);
        if (File.Exists(bundled))
        {
            _executable = bundled;
            _arguments = configArguments;
            // Le bundle PyInstaller cherche son dossier _internal relativement à l'exécutable.
            _workingDir = Path.GetDirectoryName(bundled);
            return true;
        }

        // Le parent de dataPath est la racine du projet (éditeur) ou du build (standalone).
        string scriptPath = appRoot != null ? Path.Combine(appRoot, ScriptRelativePath) : null;
        if (scriptPath != null && File.Exists(scriptPath))
        {
            _executable = "/bin/bash";
            _arguments = $"\"{scriptPath}\" {configArguments}";
            _workingDir = Path.GetDirectoryName(scriptPath);
            return true;
        }

        return false;
    }

    private void LaunchProcess()
    {
        _restartQueued = false;

        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            Arguments = _arguments,
            WorkingDirectory = _workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[bridge] {e.Data}"); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[bridge] {e.Data}"); };

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _launchTime = Time.realtimeSinceStartup;
            Debug.Log($"[KinectBridge] Bridge lancé (pid {_process.Id}).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[KinectBridge] Impossible de lancer le bridge : {e.Message}");
            _process = null;
        }
    }

    private void Update()
    {
        if (_quitting) return;

        if (_process != null)
        {
            // Stable un moment : incident précédent oublié, quota de tentatives réarmé.
            if (Time.realtimeSinceStartup - _launchTime > stableUptimeSeconds)
            {
                _restartAttempts = 0;
            }

            if (_process.HasExited)
            {
                int code = _process.ExitCode;
                _process.Dispose();
                _process = null;

                if (_restartAttempts >= maxRestartAttempts)
                {
                    Debug.LogError($"[KinectBridge] Bridge arrêté (code {code}) et " +
                                    $"{maxRestartAttempts} tentatives de relance épuisées. Clavier " +
                                    "disponible ; relancer le jeu pour réessayer.");
                    return;
                }

                _restartAttempts++;
                Debug.LogWarning($"[KinectBridge] Bridge arrêté (code {code}). Relance dans " +
                                  $"{restartDelaySeconds:0.#}s (tentative {_restartAttempts}/{maxRestartAttempts})...");
                _restartQueued = true;
                _restartAt = Time.realtimeSinceStartup + restartDelaySeconds;
            }
        }

        if (_restartQueued && Time.realtimeSinceStartup >= _restartAt)
        {
            LaunchProcess();
        }
    }

    private void OnApplicationQuit()
    {
        _quitting = true;
        KillProcess();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        KillProcess();
    }

    private void KillProcess()
    {
        if (_process == null) return;
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
