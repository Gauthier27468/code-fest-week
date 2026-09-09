using UnityEngine;
using UnityEngine.InputSystem;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Démarrage au clavier : Espace tenu joue le même rôle que la pose bras tendus devant la
    /// Kinect. Toujours disponible pour les tests et pour les animateurs.
    /// </summary>
    public class DemoKeyboardInput : MonoBehaviour
    {
        /// <summary>Équivalent clavier de la posture bras tendus.</summary>
        public bool IsGlideHeld
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.spaceKey.isPressed;
            }
        }
    }
}
