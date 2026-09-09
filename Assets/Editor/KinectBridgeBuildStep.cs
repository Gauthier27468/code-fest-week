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
/// du build, juste après que Unity l'a produit.
///
/// Objectif : un seul dossier à copier sur la machine de démo. Plus besoin de trimballer
/// tools/kinect_bridge à côté de l'exécutable, ni d'avoir Python ou uv installés là-bas.
///
/// Pourquoi en POST-process et pas en pre-process : mettre les ~365 Mo du bundle dans
/// Assets/StreamingAssets ferait générer à Unity des milliers de fichiers .meta et ralentirait
/// l'éditeur à chaque import. En post-process on écrit directement dans le dossier de build, sans
/// jamais faire transiter le bundle par le projet.
///
/// La compilation prend ~20 s. Pour l'éviter sur des builds de test rapides, définir la variable
/// d'environnement KIBIRD_SKIP_BRIDGE_BUILD=1 avant de lancer Unity.
/// </summary>
public class KinectBridgeBuildStep : IPostprocessBuildWithReport
{
    private const string SkipEnvVar = "KIBIRD_SKIP_BRIDGE_BUILD";
    private const string BuildScriptRelativePath = "tools/kinect_bridge/build_bridge.sh";
    private const string DestFolderName = "kinect_bridge";

    // Assez large pour une compilation à froid (téléchargement du modèle + venv à créer) sans
    // laisser l'éditeur bloqué indéfiniment si PyInstaller part en vrille.
    private const int TimeoutMs = 10 * 60 * 1000;

    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        // Le bridge est un binaire Linux natif (freenect + libfreenect) : le geler n'a de sens
        // que pour un build Linux, la cible de la machine de démo.
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
            // Poste de dev sans le dossier des outils : on n'a rien à geler, et le jeu reste
            // parfaitement jouable au clavier. Pas de quoi faire échouer le build.
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
    }

    /// <summary>
    /// &lt;dossier du build&gt;/&lt;Exécutable&gt;_Data/StreamingAssets/kinect_bridge, déduit du chemin de
    /// l'exécutable produit : Unity nomme le dossier de données d'après lui, pas d'après
    /// Application.productName (qui peut contenir des espaces ou différer).
    /// </summary>
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

                // Lecture avant WaitForExit : les deux flux sont lus jusqu'au bout, sinon un
                // bundle bavard peut saturer le tampon du pipe et bloquer PyInstaller pour de bon.
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
                    // Échec dur : un build de JPO sans bridge est inutilisable, mieux vaut le
                    // savoir tout de suite que le découvrir devant les visiteurs.
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
