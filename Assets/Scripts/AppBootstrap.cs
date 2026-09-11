using UnityEngine;

namespace KiBird
{
    /// <summary>
    /// Cale le jeu sur 60 Hz avec vSync avant le chargement de la première scène. Sans ça le
    /// rendu part à plusieurs centaines de FPS : tearing sur le vidéoprojecteur, ratio rendu /
    /// pas de physique instable, et machine qui chauffe toute la journée pour rien.
    /// </summary>
    public static class AppBootstrap
    {
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            QualitySettings.vSyncCount = 1;
            // Filet de sécurité : sous Linux le vSync n'est pas toujours honoré selon le
            // compositeur et le pilote. Ignoré quand le vSync fonctionne.
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
