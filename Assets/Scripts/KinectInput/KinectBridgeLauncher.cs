using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Lance et supervise le bridge Kinect en même temps que le jeu : un seul exécutable à démarrer
/// pour l'animateur, plus besoin d'ouvrir un terminal.
///
/// Deux sources possibles, essayées dans cet ordre :
///  1. le binaire gelé embarqué dans les StreamingAssets du build (déposé par
///     Assets/Editor/KinectBridgeBuildStep.cs) — le cas normal sur la machine de démo, où ni
///     Python ni uv ne sont installés ;
///  2. tools/kinect_bridge/run_bridge.sh à côté du projet — le cas du poste de dev, où l'on veut
///     lancer le code source tel quel sans repasser par une compilation PyInstaller.
///
/// Dans les deux cas le process lancé EST le bridge (run_bridge.sh se termine par un `exec`) :
/// le tuer ici tue directement Python, sans laisser de zombie tenant la Kinect ou le port UDP.
///
/// Aucune scène à modifier : comme KinectInputSource, ce composant se crée tout seul au
/// lancement. S'il ne trouve aucune des deux sources, il se désactive silencieusement — le
/// clavier reste disponible comme d'habitude.
/// </summary>
public class KinectBridgeLauncher : MonoBehaviour
{
    private const string ScriptRelativePath = "tools/kinect_bridge/run_bridge.sh";
    private const string BundledBridgeRelativePath = "kinect_bridge/kibird_bridge";

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

        // Le bridge (freenect, systemd, gspca_kinect...) est spécifique à la machine de démo
        // Linux : sur toute autre plateforme il n'y a rien à lancer, le clavier suffit.
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

    /// <summary>
    /// Choisit le bridge à lancer : binaire gelé embarqué en priorité, script source en repli.
    /// Renseigne _executable / _arguments / _workingDir et renvoie false si rien n'est utilisable.
    /// </summary>
    private bool ResolveBridgeCommand()
    {
        string bundled = Path.Combine(Application.streamingAssetsPath, BundledBridgeRelativePath);
        if (File.Exists(bundled))
        {
            _executable = bundled;
            _arguments = string.Empty;
            // Le bundle PyInstaller cherche son dossier _internal relativement à l'exécutable :
            // on se place dedans pour rester au plus près de son fonctionnement normal.
            _workingDir = Path.GetDirectoryName(bundled);
            return true;
        }

        // Application.dataPath = <Build>/<Produit>_Data en standalone, <Projet>/Assets dans
        // l'éditeur : dans les deux cas son parent est l'endroit où trouver tools/kinect_bridge.
        string appRoot = Directory.GetParent(Application.dataPath)?.FullName;
        string scriptPath = appRoot != null ? Path.Combine(appRoot, ScriptRelativePath) : null;
        if (scriptPath != null && File.Exists(scriptPath))
        {
            _executable = "/bin/bash";
            _arguments = $"\"{scriptPath}\"";
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
            // Une fois stable un moment, on considère l'incident précédent oublié : les
            // prochaines coupures repartent avec le plein quota de tentatives.
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
            // déjà mort entre le HasExited et le Kill : rien à faire.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
