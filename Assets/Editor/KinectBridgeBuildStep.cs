using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Gèle le bridge Python en binaire autonome (PyInstaller) et le dépose dans les StreamingAssets
/// du build : un seul dossier à copier sur la machine de démo, sans Python ni uv installés.
/// En POST-process pour ne jamais faire transiter les ~365 Mo du bundle par Assets/, ce qui
/// génèrerait des milliers de .meta. KIBIRD_SKIP_BRIDGE_BUILD=1 saute l'étape (~20 s).
/// </summary>
public class KinectBridgeBuildStep : IPostprocessBuildWithReport
{
    private const string SkipEnvVar = "KIBIRD_SKIP_BRIDGE_BUILD";
    private const string BuildScriptRelativePath = "tools/kinect_bridge/build_bridge.sh";
    private const string DestFolderName = "kinect_bridge";
    private const string ConfigFileName = "kibird-config.toml";

    // Assez large pour une compilation à froid (modèle à télécharger, venv à créer).
    private const int TimeoutMs = 10 * 60 * 1000;

    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        // Binaire Linux natif : le geler n'a de sens que pour un build Linux.
        if (report.summary.platform != BuildTarget.StandaloneLinux64)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SkipEnvVar)))
        {
            Debug.Log($"[KinectBridge] {SkipEnvVar} défini : compilation du bridge sautée. " +
                      "Le build n'aura PAS de bridge embarqué (clavier uniquement).");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (projectRoot == null)
        {
            Debug.LogWarning("[KinectBridge] Racine du projet introuvable : bridge non embarqué.");
            return;
        }

        string buildScript = Path.Combine(projectRoot, BuildScriptRelativePath);
        if (!File.Exists(buildScript))
        {
            // Poste de dev sans le dossier des outils : rien à geler, le clavier suffit.
            Debug.LogWarning($"[KinectBridge] '{BuildScriptRelativePath}' introuvable : bridge non " +
                             "embarqué, le build fonctionnera au clavier uniquement.");
            return;
        }

        string destination = ResolveStreamingAssetsPath(report);
        if (destination == null)
        {
            Debug.LogWarning("[KinectBridge] Dossier de build illisible : bridge non embarqué.");
            return;
        }

        Debug.Log($"[KinectBridge] Compilation du bridge vers {destination} (~20 s)...");
        RunBuildScript(buildScript, destination);
        CopyConfigNextToBuild(projectRoot, report.summary.outputPath);
    }

    private static void CopyConfigNextToBuild(string projectRoot, string outputPath)
    {
        string source = Path.Combine(projectRoot, ConfigFileName);
        string buildDir = Path.GetDirectoryName(outputPath);
        if (!File.Exists(source) || buildDir == null) return;

        string destination = Path.Combine(buildDir, ConfigFileName);
        File.Copy(source, destination, true);
        Debug.Log($"[KinectBridge] Configuration copiee vers {destination}.");
    }

    /// <summary>Déduit du chemin de l'exécutable produit : Unity nomme le dossier de données
    /// d'après lui, pas d'après Application.productName.</summary>
    private static string ResolveStreamingAssetsPath(BuildReport report)
    {
        string outputPath = report.summary.outputPath;
        if (string.IsNullOrEmpty(outputPath)) return null;

        string buildDir = Path.GetDirectoryName(outputPath);
        string executableName = Path.GetFileNameWithoutExtension(outputPath);
        if (buildDir == null || string.IsNullOrEmpty(executableName)) return null;

        return Path.Combine(buildDir, executableName + "_Data", "StreamingAssets", DestFolderName);
    }

    private static void RunBuildScript(string scriptPath, string destination)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\" \"{destination}\"",
            WorkingDirectory = Path.GetDirectoryName(scriptPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new BuildFailedException("[KinectBridge] Impossible de démarrer build_bridge.sh.");
                }

                // Lecture avant WaitForExit : sinon un bundle bavard sature le tampon du pipe.
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(TimeoutMs))
                {
                    try { process.Kill(); } catch { /* déjà mort */ }
                    throw new BuildFailedException(
                        $"[KinectBridge] Compilation du bridge interrompue après {TimeoutMs / 60000} min.");
                }

                if (!string.IsNullOrWhiteSpace(stdout)) Debug.Log($"[KinectBridge] {stdout.Trim()}");

                if (process.ExitCode != 0)
                {
                    // Échec dur : un build sans bridge est inutilisable le jour J.
                    throw new BuildFailedException(
                        $"[KinectBridge] build_bridge.sh a échoué (code {process.ExitCode}) :\n{stderr.Trim()}\n" +
                        $"Pour builder quand même sans bridge, définir {SkipEnvVar}=1.");
                }

                if (!string.IsNullOrWhiteSpace(stderr)) Debug.Log($"[KinectBridge] {stderr.Trim()}");
                Debug.Log("[KinectBridge] Bridge embarqué dans les StreamingAssets du build.");
            }
        }
        catch (BuildFailedException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new BuildFailedException($"[KinectBridge] Compilation du bridge impossible : {e.Message}");
        }
    }
}
