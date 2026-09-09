using System.Collections;
using UnityEngine;

/// <summary>
/// Gère le franchissement des anneaux (cerceaux) par le joueur.
/// - Attribue 100 points bonus au score du joueur.
/// - Fournit un feedback visuel immédiat (éclats de particules dorées, rétractation).
/// - Empêche le multi-déclenchement et le passage à travers sans détection (anti-tunneling).
/// </summary>
public class HoopScore : MonoBehaviour
{
    [Header("Score")]
    [Tooltip("Nombre de points bonus attribués lors du franchissement de l'anneau (100 pts par défaut).")]
    public int bonusPoints = 100;

    [Header("Audio & Effets")]
    [Tooltip("Son joué lors du franchissement (optionnel, auto-chargé si vide).")]
    public AudioClip collectSound;

    [Tooltip("Volume du son de collecte.")]
    [Range(0f, 1f)]
    public float soundVolume = 1.0f;

    [Tooltip("Effet de particules personnalisé instancié à la collecte (optionnel, auto-généré si vide).")]
    public GameObject collectEffectPrefab;

    private bool isCollected = false;
    private Collider triggerCollider;

    private void Awake()
    {
        if (collectSound == null)
        {
#if UNITY_EDITOR
            collectSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AssetStore/Sounds/hoop_collect.wav");
#endif
        }

        triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    public void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        // Détection du joueur (tag Player ou script MoveBird sur l'objet / ses parents)
        if (other.CompareTag("Player") || other.GetComponentInParent<MoveBird>() != null || other.GetComponent<MoveBird>() != null)
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

        // Attribution des 100 points
        MoveBird.AddBonusScore(bonusPoints);
        Debug.Log($"[HoopScore] Anneau franchi ! +{bonusPoints} pts | Nouveau score : {MoveBird.CurrentScore}");

        // Son de collecte
        if (collectSound != null)
        {
            Vector3 soundPos = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioSource.PlayClipAtPoint(collectSound, soundPos, soundVolume);
        }

        // Effet visuel de collecte
        if (collectEffectPrefab != null)
        {
            Instantiate(collectEffectPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            SpawnCollectSparkles();
        }

        // Animation de disparition fluide
        StartCoroutine(AnimateCollectionAndDestroy());
    }

    private void SpawnCollectSparkles()
    {
        GameObject fxObj = new GameObject("HoopCollectFX");
        fxObj.transform.position = transform.position;

        ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();

        Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
        if (pShader != null)
        {
            psr.material = new Material(pShader);
        }

        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startColor = new Color(1f, 0.85f, 0.2f, 1f); // Éclats dorés
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0.0f, (short)20, (short)35)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.7f;

        ps.Play();
        Destroy(fxObj, 1.0f);
    }

    private IEnumerator AnimateCollectionAndDestroy()
    {
        Vector3 initialScale = transform.localScale;
        float duration = 0.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Gonflement instantané puis rétraction
            float scaleMultiplier = Mathf.Lerp(1.25f, 0.05f, t * t);
            transform.localScale = initialScale * scaleMultiplier;
            yield return null;
        }

        Destroy(gameObject);
    }
}
