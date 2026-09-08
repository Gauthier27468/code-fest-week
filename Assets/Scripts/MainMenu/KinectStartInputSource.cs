using UnityEngine;

namespace KiBird.MainMenu
{
    // Fournit au menu la présence et la pose du joueur à partir du flux Kinect, avec le même
    // contrat que DemoKeyboardInput (IsPlayerPresent / CurrentPose). Le menu peut ainsi être
    // démarré soit au clavier (Espace, mode debug/animateur), soit en pose "bras en T" devant
    // la Kinect — les deux fonctionnent en parallèle, aucun des deux ne remplace l'autre.
    public class KinectStartInputSource : MonoBehaviour
    {
        [Tooltip("Valeur de Glide (0 = bras baissés, 1 = bras à l'horizontale) au-delà de " +
                 "laquelle la pose 'bras en T' est considérée tenue.")]
        [SerializeField] private float glideThreshold = 0.75f;

        public bool IsPlayerPresent => KinectInputSource.Instance != null && KinectInputSource.Instance.HasPlayer;

        public BirdPose CurrentPose =>
            IsPlayerPresent && KinectInputSource.Instance.Glide >= glideThreshold
                ? BirdPose.Glide
                : BirdPose.Neutral;
    }
}
