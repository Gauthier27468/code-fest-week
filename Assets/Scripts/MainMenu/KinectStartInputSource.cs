using UnityEngine;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Expose la posture bras tendus lue sur le flux Kinect, avec le même contrat que
    /// DemoKeyboardInput. Les deux fonctionnent en parallèle, aucun ne remplace l'autre.
    /// </summary>
    public class KinectStartInputSource : MonoBehaviour
    {
        [Tooltip("Valeur de Glide (0 = bras baissés, 1 = bras à l'horizontale) au-delà de " +
                 "laquelle la pose 'bras en T' est considérée tenue.")]
        [SerializeField] private float glideThreshold = 0.75f;

        public bool IsGlideHeld =>
            KinectInputSource.Instance != null &&
            KinectInputSource.Instance.HasPlayer &&
            KinectInputSource.Instance.Glide >= glideThreshold;
    }
}
