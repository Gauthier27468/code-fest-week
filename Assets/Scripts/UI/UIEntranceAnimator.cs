using UnityEngine;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Petite animation d'apparition sans tween externe. Elle fonctionne avec le temps non mis à
    /// l'échelle pour rester fluide sur les écrans de pause et de fin de partie.
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
            rectTransform = transform as RectTransform;
            restingScale = rectTransform != null ? rectTransform.localScale : Vector3.one;
        }

        private void OnEnable()
        {
            elapsed = 0f;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (rectTransform == null) rectTransform = transform as RectTransform;

            canvasGroup.alpha = 0f;
            if (rectTransform != null) rectTransform.localScale = restingScale * startScale;
        }

        private void Update()
        {
            if (elapsed >= duration) return;

            elapsed = Mathf.Min(duration, elapsed + Time.unscaledDeltaTime);
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            canvasGroup.alpha = eased;
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.LerpUnclamped(restingScale * startScale, restingScale, eased);
            }
        }
    }
}
