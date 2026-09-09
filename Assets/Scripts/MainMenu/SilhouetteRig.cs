using UnityEngine;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Silhouette décorative du badge central : fige le personnage en posture bras tendus,
    /// la posture que le joueur doit reproduire pour lancer une partie.
    /// </summary>
    public class SilhouetteRig : MonoBehaviour
    {
        [SerializeField] private RectTransform bodyRoot;
        [SerializeField] private RectTransform leftArm;
        [SerializeField] private RectTransform rightArm;

        public void Configure(RectTransform bodyRootRT, RectTransform leftArmRT, RectTransform rightArmRT)
        {
            bodyRoot = bodyRootRT;
            leftArm = leftArmRT;
            rightArm = rightArmRT;
        }

        public void ApplyGlidePose()
        {
            if (bodyRoot != null) bodyRoot.localEulerAngles = Vector3.zero;
            if (leftArm != null) leftArm.localEulerAngles = Vector3.zero;
            if (rightArm != null) rightArm.localEulerAngles = Vector3.zero;
        }

        private void Start()
        {
            ApplyGlidePose();
        }
    }
}
