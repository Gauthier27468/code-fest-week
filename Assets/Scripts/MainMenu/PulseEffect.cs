using UnityEngine;

namespace KiBird.MainMenu
{
    // Fait légèrement grossir/rétrécir un élément d'UI en boucle, pour attirer l'oeil sur les
    // textes d'accent (score, libellés du tutoriel).
    public class PulseEffect : MonoBehaviour
    {
        [SerializeField] private float scaleAmplitude = 0.06f;
        [SerializeField] private float period = 1.2f;

        private RectTransform rectTransform;
        private Vector3 baseScale;
        private float phaseOffset;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            baseScale = rectTransform.localScale;
            phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            float t = Mathf.Sin(Time.time * Mathf.PI * 2f / period + phaseOffset);
            rectTransform.localScale = baseScale * (1f + t * scaleAmplitude);
        }
    }
}
