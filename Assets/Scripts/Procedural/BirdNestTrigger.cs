using UnityEngine;

/// <summary>
/// Déclencheur de fin de niveau placé sur le nid (Bird Nest) dans EndingBlock.
/// - Détecte l'arrivée de l'oiseau dans le nid.
/// - Arrête le vol et valide la victoire.
/// - Affiche l'écran de victoire vert avec le score final et le son de victoire.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BirdNestTrigger : MonoBehaviour
{
    [Header("Réglages de Victoire")]
    [Tooltip("Bonus de points accordé pour avoir terminé le parcours et atteint le nid.")]
    public int winBonusPoints = 500;

    [Tooltip("Effet visuel festif instancié à l'arrivée au nid (optionnel).")]
    public GameObject victoryEffectPrefab;

    [Header("Trigger Zone")]
    [Tooltip("Élargit automatiquement le BoxCollider s'il est trop petit pour faciliter l'atterrissage.")]
    public bool autoExpandCollider = true;

    [Tooltip("Taille cible du collider de détection d'arrivée.")]
    public Vector3 targetColliderSize = new Vector3(4f, 3f, 4f);

    private bool hasWon = false;

    private void OnTriggerEnter(Collider other)
    {
        if (hasWon) return;

        // Détection du joueur
        MoveBird bird = other.GetComponent<MoveBird>() 
                     ?? other.GetComponentInParent<MoveBird>();

        if (bird != null && other.CompareTag("Player"))
        {
            hasWon = true;
            Debug.Log("[BirdNestTrigger] Le joueur a atteint le nid ! (" + other.gameObject.name + ")");
            Debug.Log("[BirdNestTrigger] " + other.name);

            // Déclenche la victoire sur l'oiseau
            if (bird != null)
            {
                bird.Win(winBonusPoints);
            }
            else
            {
                // Fallback direct sur GameOverUI
                if (GameOverUI.Instance != null)
                {
                    GameOverUI.Instance.ShowVictory();
                }
            }

            // Effet festif
            if (victoryEffectPrefab != null)
            {
                Instantiate(victoryEffectPrefab, transform.position, Quaternion.identity);
            }
            else
            {
                SpawnVictoryConfetti();
            }
        }
    }

    private void SpawnVictoryConfetti()
    {
        GameObject fx = new GameObject("VictoryConfetti");
        fx.transform.position = transform.position + Vector3.up * 0.5f;

        ParticleSystem ps = fx.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psr = fx.GetComponent<ParticleSystemRenderer>();

        Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") 
                      ?? Shader.Find("Particles/Standard Unlit") 
                      ?? Shader.Find("Sprites/Default");
        if (pShader != null) psr.material = new Material(pShader);

        var main = ps.main;
        main.duration = 1.0f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.gravityModifier = 0.3f;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0.0f, (short)50, (short)80)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.0f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.2f, 0.95f, 0.4f), 0.0f), // Vert émeraude
                new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.5f),   // Or
                new GradientColorKey(new Color(0.2f, 0.8f, 1f), 1.0f)     // Cyan
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.8f, 0.7f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = grad;

        ps.Play();
        Destroy(fx, 3f);
    }

}
