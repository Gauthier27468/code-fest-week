using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Allume les panneaux du tutoriel l'un après l'autre : quand l'oiseau sort du trigger d'un
/// panneau allumé, celui-ci s'éteint et le panneau suivant sur l'axe Z s'allume.
/// </summary>
public class SignGlower : MonoBehaviour
{
    private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
    private static readonly int ActivationTimeId = Shader.PropertyToID("_ActivationTime");

    [Header("Trigger")]
    [Tooltip("Zone de passage (Trigger). Si vide, cherchée parmi les colliders enfants.")]
    [SerializeField] private Collider passageTriggerCollider;

    [Header("Options")]
    [Tooltip("Allumé dès le départ. Le premier panneau sur l'axe Z s'allume de toute façon.")]
    [SerializeField] private bool startGlowing = false;

    [Tooltip("Durée de montée progressive du glow (en secondes), pour éviter un flash lumineux.")]
    [SerializeField] private float fadeInDuration = 0.4f;

    [SerializeField] private string playerTag = "Player";

    /// <summary>Matériau et ses valeurs de glow d'origine (à pleine intensité).</summary>
    private struct CachedMaterial
    {
        public Material material;
        public float glowStrength;
        public Color emissionColor;
        public Color neonColor;
    }

    private readonly List<CachedMaterial> cachedMaterials = new List<CachedMaterial>();
    private Coroutine fadeCoroutine;
    private bool isGlowing;
    private bool hasTriggeredExit;

    private void Awake()
    {
        SetupTrigger();
        CacheMaterials();
    }

    private void Start()
    {
        if (!startGlowing && IsFirstSignOnZ()) startGlowing = true;
        SetGlow(startGlowing, animate: false);
    }

    private void OnDisable()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        // Les matériaux sont des copies créées par renderer.materials : à libérer.
        foreach (CachedMaterial data in cachedMaterials)
        {
            if (data.material != null) Destroy(data.material);
        }
        cachedMaterials.Clear();
    }

    // ------------------------------------------------------------------ mise en place

    private void SetupTrigger()
    {
        if (passageTriggerCollider == null)
        {
            // Premier trigger trouvé ; à défaut le 2e collider (le 1er étant en général celui du
            // panneau lui-même), ou le seul disponible.
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider col in colliders)
            {
                if (col.isTrigger)
                {
                    passageTriggerCollider = col;
                    break;
                }
            }
            if (passageTriggerCollider == null && colliders.Length > 0)
            {
                passageTriggerCollider = colliders[Mathf.Min(1, colliders.Length - 1)];
            }
        }

        if (passageTriggerCollider == null) return;

        passageTriggerCollider.isTrigger = true;

        // Trigger sur un autre objet : un relais lui fait remonter OnTriggerExit jusqu'ici.
        if (passageTriggerCollider.gameObject != gameObject)
        {
            SignTriggerProxy proxy = passageTriggerCollider.GetComponent<SignTriggerProxy>();
            if (proxy == null) proxy = passageTriggerCollider.gameObject.AddComponent<SignTriggerProxy>();
            proxy.Init(this);
        }
    }

    private void CacheMaterials()
    {
        Renderer ownRenderer = GetComponent<Renderer>();
        Renderer[] renderers = ownRenderer != null ? new[] { ownRenderer } : GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in renderers)
        {
            foreach (Material mat in r.materials)
            {
                if (mat == null) continue;

                var data = new CachedMaterial
                {
                    material = mat,
                    glowStrength = mat.HasProperty(GlowStrengthId) ? mat.GetFloat(GlowStrengthId) : 0f,
                    emissionColor = mat.HasProperty(EmissionColorId) ? mat.GetColor(EmissionColorId) : Color.black,
                    neonColor = mat.HasProperty(NeonColorId) ? mat.GetColor(NeonColorId) : Color.black
                };

                // Un glow nul à l'origine resterait invisible une fois « allumé ».
                if (data.glowStrength <= 0f && mat.HasProperty(GlowStrengthId)) data.glowStrength = 0.1f;

                cachedMaterials.Add(data);
            }
        }
    }

    // ------------------------------------------------------------------ allumage

    public void SetGlow(bool active, bool animate = true)
    {
        isGlowing = active;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        if (!active)
        {
            SetGlowIntensity(0f);
            return;
        }

        // Horodatage d'activation, pour les shaders qui animent l'allumage via _ActivationTime.
        foreach (CachedMaterial data in cachedMaterials)
        {
            if (data.material != null && data.material.HasProperty(ActivationTimeId))
            {
                data.material.SetFloat(ActivationTimeId, Time.time);
            }
        }

        if (animate && fadeInDuration > 0f && gameObject.activeInHierarchy)
        {
            fadeCoroutine = StartCoroutine(FadeInRoutine());
        }
        else
        {
            SetGlowIntensity(1f);
        }
    }

    /// <summary>Monte le glow de 0 à 1 en douceur (smoothstep).</summary>
    private IEnumerator FadeInRoutine()
    {
        float elapsed = 0f;
        SetGlowIntensity(0f);

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetGlowIntensity(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration)));
            yield return null;
        }

        SetGlowIntensity(1f);
        fadeCoroutine = null;
    }

    /// <summary>0 = éteint (émission coupée), 1 = glow d'origine.</summary>
    private void SetGlowIntensity(float factor)
    {
        bool on = factor > 0f;
        foreach (CachedMaterial data in cachedMaterials)
        {
            Material m = data.material;
            if (m == null) continue;

            if (m.HasProperty(GlowStrengthId)) m.SetFloat(GlowStrengthId, on ? data.glowStrength * factor : 0f);
            if (m.HasProperty(EmissionColorId)) m.SetColor(EmissionColorId, on ? data.emissionColor : Color.black);
            if (m.HasProperty(NeonColorId)) m.SetColor(NeonColorId, on ? data.neonColor : Color.black);

            if (on) m.EnableKeyword("_EMISSION");
            else m.DisableKeyword("_EMISSION");
        }
    }

    // ------------------------------------------------------------------ passage de l'oiseau

    private void OnTriggerExit(Collider other) => HandleTriggerExit(other);

    public void HandleTriggerExit(Collider other)
    {
        if (hasTriggeredExit || !isGlowing) return;
        if (!other.CompareTag(playerTag) && other.GetComponentInParent<MoveBird>() == null) return;

        hasTriggeredExit = true;
        SetGlow(false);

        SignGlower nextSign = FindNextSignOnZ();
        if (nextSign != null) nextSign.SetGlow(true);
    }

    /// <summary>Panneau le plus proche devant celui-ci sur l'axe Z.</summary>
    private SignGlower FindNextSignOnZ()
    {
        SignGlower nextSign = null;
        float myZ = transform.position.z;
        float minDeltaZ = float.MaxValue;

        foreach (SignGlower sign in FindObjectsByType<SignGlower>())
        {
            float deltaZ = sign.transform.position.z - myZ;
            if (sign != this && deltaZ > 0.01f && deltaZ < minDeltaZ)
            {
                minDeltaZ = deltaZ;
                nextSign = sign;
            }
        }
        return nextSign;
    }

    private bool IsFirstSignOnZ()
    {
        float myZ = transform.position.z;
        foreach (SignGlower sign in FindObjectsByType<SignGlower>())
        {
            if (sign != this && sign.transform.position.z < myZ - 0.01f) return false;
        }
        return true;
    }
}

/// <summary>Relaie OnTriggerExit d'un collider enfant vers son SignGlower.</summary>
public class SignTriggerProxy : MonoBehaviour
{
    private SignGlower owner;

    public void Init(SignGlower glower) => owner = glower;

    private void OnTriggerExit(Collider other)
    {
        if (owner != null) owner.HandleTriggerExit(other);
    }
}
