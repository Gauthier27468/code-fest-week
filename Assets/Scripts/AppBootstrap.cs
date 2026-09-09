using UnityEngine;

namespace KiBird
{
    /// <summary>
    /// Réglages globaux appliqués avant le chargement de la première scène.
    ///
    /// Le projet est livré avec vSyncCount = 0 sur tous les niveaux de qualité et sans
    /// targetFrameRate : le jeu tourne alors à plusieurs centaines de FPS sur une machine récente.
    /// Ce n'est pas souhaitable pour la JPO :
    ///  - le rendu part en tearing sur le vidéoprojecteur ;
    ///  - le ratio frames de rendu / pas de physique (50 Hz) devient énorme et instable, ce qui
    ///    dégrade la régularité du déplacement de l'oiseau ;
    ///  - la machine chauffe pour rien pendant une journée entière de démonstration.
    /// </summary>
    public static class AppBootstrap
    {
        /// <summary>Cadence visée. 60 Hz correspond à la sortie d'un vidéoprojecteur standard.</summary>
        private const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // vSync = 1 : cadence calée sur le rafraîchissement de l'écran, pas de tearing.
            QualitySettings.vSyncCount = 1;

            // Filet de sécurité : sous Linux, selon le compositeur et le pilote, le vSync n'est pas
            // toujours honoré et la boucle de rendu part alors en roue libre. targetFrameRate prend
            // le relais dans ce cas (il est simplement ignoré quand le vSync fonctionne).
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
