using System.Collections;
using UnityEngine;
using KiBird.FX;

/// <summary>
/// Anneau bonus : accorde des points au franchissement, joue un son et disparaît en se rétractant.
/// </summary>
public class HoopScore : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Points bonus attribués au franchissement de l'anneau.")]
    public int bonusPoints = 100;

    [Header("Audio & Effets")]
    [Tooltip("Son joué au franchissement.")]
    public AudioClip collectSound;

    [Range(0f, 1f)]
    public float soundVolume = 1.0f;

    [Tooltip("Effet de particules à la collecte. Si vide, des confettis multicolores sont générés.")]
    public GameObject collectEffectPrefab;

    private bool isCollected;
    private Collider triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        // GetComponentInParent inclut l'objet lui-même.
        if (other.CompareTag("Player") || other.GetComponentInParent<MoveBird>() != null)
        {
            CollectHoop();
        }
    }

    private void CollectHoop()
    {
        isCollected = true;

        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }

        MoveBird.AddBonusScore(bonusPoints);

        if (collectSound != null)
        {
            Vector3 soundPos = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioSource.PlayClipAtPoint(collectSound, soundPos, soundVolume);
        }

        ParticleBurst.Play(transform.position,
            ParticleBurst.HoopConfetti, collectEffectPrefab);

        StartCoroutine(AnimateCollectionAndDestroy());
    }

    private IEnumerator AnimateCollectionAndDestroy()
    {
        Vector3 initialScale = transform.localScale;
        const float duration = 0.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Gonflement instantané puis rétraction.
            transform.localScale = initialScale * Mathf.Lerp(1.25f, 0.05f, t * t);
            yield return null;
        }

        Destroy(gameObject);
    }
}
