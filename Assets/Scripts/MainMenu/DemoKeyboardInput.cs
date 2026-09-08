using UnityEngine;
using UnityEngine.InputSystem;

namespace KiBird.MainMenu
{
    // Entrée de démonstration en attendant le module Kinect.
    // Espace = bras à l'horizontale (pose de démarrage), Gauche/Droite = inclinaison,
    // Haut = battement des bras. La présence du joueur est simulée par l'appui sur une
    // touche ; le vrai module Kinect exposera IsPlayerPresent via la détection du squelette.
    // À remplacer par un fournisseur équivalent qui lit les données Kinect.
    // Utilise le nouveau Input System (Keyboard.current) : ce projet a l'Active Input
    // Handling réglé sur "Input System Package" uniquement, la classe UnityEngine.Input
    // legacy y est désactivée.
    public class DemoKeyboardInput : MonoBehaviour
    {
        public BirdPose CurrentPose { get; private set; } = BirdPose.Neutral;
        public bool IsPlayerPresent { get; private set; }

        private float lastInputTime = -999f;
        private const float PresenceTimeout = 2f;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                IsPlayerPresent = false;
                CurrentPose = BirdPose.Neutral;
                return;
            }

            bool anyKey = keyboard.spaceKey.isPressed || keyboard.leftArrowKey.isPressed ||
                          keyboard.rightArrowKey.isPressed || keyboard.aKey.isPressed ||
                          keyboard.dKey.isPressed || keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed;

            if (anyKey) lastInputTime = Time.time;
            IsPlayerPresent = Time.time - lastInputTime < PresenceTimeout;

            if (!IsPlayerPresent)
            {
                CurrentPose = BirdPose.Neutral;
                return;
            }

            if (keyboard.spaceKey.isPressed)
                CurrentPose = BirdPose.Glide;
            else if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed)
                CurrentPose = BirdPose.FlapUp;
            else if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed)
                CurrentPose = BirdPose.TiltLeft;
            else if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed)
                CurrentPose = BirdPose.TiltRight;
            else
                CurrentPose = BirdPose.Neutral;
        }
    }
}
