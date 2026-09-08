using UnityEngine;

namespace KiBird.MainMenu
{
    // Silhouette procédurale (corps + deux bras) qui illustre une posture du joueur.
    // Construite et câblée par KiBirdMenuBuilder ; sera pilotée plus tard par le module Kinect
    // via SetPose(), à la place de DemoKeyboardInput.
    public class SilhouetteRig : MonoBehaviour
    {
        [SerializeField] private RectTransform bodyRoot;
        [SerializeField] private RectTransform leftArm;
        [SerializeField] private RectTransform rightArm;
        [SerializeField] private float lerpSpeed = 8f;

        private const float NeutralArmAngle = 65f;
        private const float GlideArmAngle = 0f;
        private const float FlapArmAngle = -75f;
        private const float TiltRootAngle = 22f;

        private float targetArmAngle;
        private float targetRootAngle;
        private float currentArmAngle;
        private float currentRootAngle;

        public void Configure(RectTransform bodyRootRT, RectTransform leftArmRT, RectTransform rightArmRT)
        {
            bodyRoot = bodyRootRT;
            leftArm = leftArmRT;
            rightArm = rightArmRT;
        }

        public void SetPose(BirdPose pose)
        {
            GetAnglesForPose(pose, out targetArmAngle, out targetRootAngle);
        }

        public void SetPoseInstant(BirdPose pose)
        {
            SetPose(pose);
            currentArmAngle = targetArmAngle;
            currentRootAngle = targetRootAngle;
            Apply();
        }

        // Pour les icônes statiques du tutoriel : pas besoin d'un MonoBehaviour qui lerp
        // à chaque frame, on fige directement la posture voulue sur des RectTransforms.
        public static void ApplyStaticPose(RectTransform body, RectTransform leftArm, RectTransform rightArm,
            BirdPose pose)
        {
            GetAnglesForPose(pose, out float armAngle, out float rootAngle);
            if (body != null) body.localEulerAngles = new Vector3(0f, 0f, rootAngle);
            if (leftArm != null) leftArm.localEulerAngles = new Vector3(0f, 0f, armAngle);
            if (rightArm != null) rightArm.localEulerAngles = new Vector3(0f, 0f, -armAngle);
        }

        private static void GetAnglesForPose(BirdPose pose, out float armAngle, out float rootAngle)
        {
            switch (pose)
            {
                case BirdPose.Neutral:
                    armAngle = NeutralArmAngle;
                    rootAngle = 0f;
                    break;
                case BirdPose.Glide:
                    armAngle = GlideArmAngle;
                    rootAngle = 0f;
                    break;
                case BirdPose.TiltLeft:
                    armAngle = GlideArmAngle;
                    rootAngle = TiltRootAngle;
                    break;
                case BirdPose.TiltRight:
                    armAngle = GlideArmAngle;
                    rootAngle = -TiltRootAngle;
                    break;
                case BirdPose.FlapUp:
                    armAngle = FlapArmAngle;
                    rootAngle = 0f;
                    break;
                default:
                    armAngle = GlideArmAngle;
                    rootAngle = 0f;
                    break;
            }
        }

        private void Update()
        {
            currentArmAngle = Mathf.Lerp(currentArmAngle, targetArmAngle, Time.deltaTime * lerpSpeed);
            currentRootAngle = Mathf.Lerp(currentRootAngle, targetRootAngle, Time.deltaTime * lerpSpeed);
            Apply();
        }

        private void Apply()
        {
            if (bodyRoot != null) bodyRoot.localEulerAngles = new Vector3(0f, 0f, currentRootAngle);
            if (leftArm != null) leftArm.localEulerAngles = new Vector3(0f, 0f, currentArmAngle);
            if (rightArm != null) rightArm.localEulerAngles = new Vector3(0f, 0f, -currentArmAngle);
        }
    }
}
