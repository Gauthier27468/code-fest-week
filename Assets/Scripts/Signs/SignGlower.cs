using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gère l'activation séquentielle automatique du glow des panneaux le long de l'axe Z.
/// Éteint le panneau actuel lors de la sortie du trigger et allume le panneau suivant sur Z.
/// </summary>
public class SignGlower : MonoBehaviour
{
    private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
    private static readonly int ActivationTimeId = Shader.PropertyToID("_ActivationTime");

    [Header("Trigger")]
    [Tooltip("Collider servant de zone de passage (Trigger).")]
    [SerializeField] private Collider passageTriggerCollider;

    [Header("Options")]
    [Tooltip("Cocher sur le 1er panneau (ou laissé automatique selon Z).")]
    [SerializeField] private bool startGlowing = false;

    [Tooltip("Durée de montée en douceur (en secondes) pour commencer au glow le plus bas et éviter le flash lumineux.")]
    [SerializeField] private float fadeInDuration = 0.4f;

    [SerializeField] private string playerTag = "Player";

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
    private bool isInitialized;

    public bool IsGlowing => isGlowing;

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        // Si non coché explicitement, le premier panneau sur l'axe Z s'allume automatiquement
        if (!startGlowing && IsFirstSignOnZ())
        {
            startGlowing = true;
        }

        SetGlow(startGlowing, animate: false);
    }

    private void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        SetupTrigger();
        CacheMaterials();
    }

    private void SetupTrigger()
    {
        if (passageTriggerCollider == null)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider col in colliders)
            {
                if (col.isTrigger)
                {
                    passageTriggerCollider = col;
                    break;
                }
            }

            if (passageTriggerCollider == null && colliders.Length > 1)
            {
                passageTriggerCollider = colliders[1];
            }
            else if (passageTriggerCollider == null && colliders.Length == 1)
            {
                passageTriggerCollider = colliders[0];
            }
        }

        if (passageTriggerCollider != null)
        {
            passageTriggerCollider.isTrigger = true;

            if (passageTriggerCollider.gameObject != this.gameObject)
            {
                SignTriggerProxy proxy = passageTriggerCollider.GetComponent<SignTriggerProxy>();
                if (proxy == null)
                {
                    proxy = passageTriggerCollider.gameObject.AddComponent<SignTriggerProxy>();
                }
                proxy.Init(this);
            }
        }
    }

    private void CacheMaterials()
    {
        Renderer rend = GetComponent<Renderer>();
        Renderer[] renderers = rend != null ? new[] { rend } : GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            foreach (Material mat in r.materials)
            {
                if (mat == null) continue;

                CachedMaterial data = new CachedMaterial
                {
                    material = mat,
                    glowStrength = mat.HasProperty(GlowStrengthId) ? mat.GetFloat(GlowStrengthId) : 0f,
                    emissionColor = mat.HasProperty(EmissionColorId) ? mat.GetColor(EmissionColorId) : Color.black,
                    neonColor = mat.HasProperty(NeonColorId) ? mat.GetColor(NeonColorId) : Color.black
                };

                if (data.glowStrength <= 0f && mat.HasProperty(GlowStrengthId))
                {
                    data.glowStrength = 0.1f;
                }

                cachedMaterials.Add(data);
            }
        }
    }

    public void SetGlow(bool active, bool animate = true)
    {
        if (!isInitialized) Initialize();
        isGlowing = active;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        if (active)
        {
            // Transmet le timestamp d'activation au cas où le shader graph utilise _ActivationTime
            for (int i = 0; i < cachedMaterials.Count; i++)
            {
                Material m = cachedMaterials[i].material;
                if (m != null && m.HasProperty(ActivationTimeId))
                {
                    m.SetFloat(ActivationTimeId, Time.time);
                }
            }

            // Démarre au glow le plus bas (0) puis monte en douceur pour éliminer le flash
            if (animate && fadeInDuration > 0f && gameObject.activeInHierarchy)
            {
                fadeCoroutine = StartCoroutine(FadeInRoutine());
            }
            else
            {
                SetGlowIntensity(1f);
            }
        }
        else
        {
            SetGlowIntensity(0f);
        }
    }

    private System.Collections.IEnumerator FadeInRoutine()
    {
        float elapsed = 0f;
        SetGlowIntensity(0f); // Démarre au glow minimum (0)

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDuration);
            // Courbe fluide (smoothstep) pour faire monter le glow sans à-coup
            float factor = Mathf.SmoothStep(0f, 1f, t);
            SetGlowIntensity(factor);
            yield return null;
        }

        SetGlowIntensity(1f);
        fadeCoroutine = null;
    }

    private void SetGlowIntensity(float factor)
    {
        for (int i = 0; i < cachedMaterials.Count; i++)
        {
            CachedMaterial data = cachedMaterials[i];
            if (data.material == null) continue;

            if (factor > 0f)
            {
                if (data.material.HasProperty(GlowStrengthId)) data.material.SetFloat(GlowStrengthId, data.glowStrength * factor);
                if (data.material.HasProperty(EmissionColorId)) data.material.SetColor(EmissionColorId, data.emissionColor);
                if (data.material.HasProperty(NeonColorId)) data.material.SetColor(NeonColorId, data.neonColor);
                data.material.EnableKeyword("_EMISSION");
            }
            else
            {
                if (data.material.HasProperty(GlowStrengthId)) data.material.SetFloat(GlowStrengthId, 0f);
                if (data.material.HasProperty(EmissionColorId)) data.material.SetColor(EmissionColorId, Color.black);
                if (data.material.HasProperty(NeonColorId)) data.material.SetColor(NeonColorId, Color.black);
                data.material.DisableKeyword("_EMISSION");
            }
        }
    }

    private void OnDisable()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        HandleTriggerExit(other);
    }

    public void HandleTriggerExit(Collider other)
    {
        if (hasTriggeredExit || !isGlowing) return;

        bool isPlayer = other.CompareTag(playerTag) ||
                        other.GetComponentInParent<MoveBird>() != null ||
                        other.GetComponent<MoveBird>() != null;

        if (!isPlayer) return;

        hasTriggeredExit = true;

        // 1. Coupe le glow de ce panneau
        SetGlow(false);

        // 2. Trouve et allume dynamiquement le panneau suivant sur l'axe Z
        SignGlower nextSign = FindNextSignOnZ();
        if (nextSign != null)
        {
            nextSign.SetGlow(true);
        }
    }

    private SignGlower FindNextSignOnZ()
    {
        SignGlower[] allSigns = FindObjectsByType<SignGlower>(FindObjectsSortMode.None);
        SignGlower nextSign = null;
        float myZ = transform.position.z;
        float minDeltaZ = float.MaxValue;

        foreach (SignGlower sign in allSigns)
        {
            if (sign == this) continue;

            float deltaZ = sign.transform.position.z - myZ;
            if (deltaZ > 0.01f && deltaZ < minDeltaZ)
            {
                minDeltaZ = deltaZ;
                nextSign = sign;
            }
        }

        return nextSign;
    }

    private bool IsFirstSignOnZ()
    {
        SignGlower[] allSigns = FindObjectsByType<SignGlower>(FindObjectsSortMode.None);
        float myZ = transform.position.z;

        foreach (SignGlower sign in allSigns)
        {
            if (sign != this && sign.transform.position.z < myZ - 0.01f)
            {
                return false;
            }
        }

        return true;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < cachedMaterials.Count; i++)
        {
            if (cachedMaterials[i].material != null)
            {
                Destroy(cachedMaterials[i].material);
            }
        }
        cachedMaterials.Clear();
    }
}

public class SignTriggerProxy : MonoBehaviour
{
    private SignGlower owner;

    public void Init(SignGlower glower) => owner = glower;

    private void OnTriggerExit(Collider other)
    {
        if (owner != null) owner.HandleTriggerExit(other);
    }
}
