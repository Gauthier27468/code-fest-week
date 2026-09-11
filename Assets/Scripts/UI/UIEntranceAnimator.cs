using UnityEngine;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Apparition en fondu + léger zoom à chaque activation de l'élément, sans librairie de tween.
    /// Utilise le temps non mis à l'échelle pour rester fluide sur les écrans de pause et de fin.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIEntranceAnimator : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float duration = 0.42f;
        [SerializeField, Range(0.8f, 1f)] private float startScale = 0.965f;

        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;
        private Vector3 restingScale;
        private float elapsed;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = (RectTransform)transform;
            restingScale = rectTransform.localScale;
        }

        private void OnEnable()
        {
            elapsed = 0f;
            canvasGroup.alpha = 0f;
            rectTransform.localScale = restingScale * startScale;
        }

        private void Update()
        {
            if (elapsed >= duration) return;

            elapsed = Mathf.Min(duration, elapsed + Time.unscaledDeltaTime);
            float t = elapsed / duration;
            float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubique

            canvasGroup.alpha = eased;
            rectTransform.localScale = Vector3.LerpUnclamped(restingScale * startScale, restingScale, eased);
        }
    }
}
