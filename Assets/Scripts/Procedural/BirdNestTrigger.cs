using UnityEngine;
using KiBird.FX;

/// <summary>
/// Déclencheur de fin de niveau posé sur le nid du bloc de fin : valide la victoire du joueur.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BirdNestTrigger : MonoBehaviour
{
    [Tooltip("Bonus accordé pour avoir terminé le parcours et atteint le nid.")]
    public int winBonusPoints = 500;

    [Tooltip("Effet visuel à l'arrivée au nid. Si vide, des confettis sont générés.")]
    public GameObject victoryEffectPrefab;

    private bool hasWon;

    private void OnTriggerEnter(Collider other)
    {
        if (hasWon || !other.CompareTag("Player")) return;

        // GetComponentInParent inclut l'objet lui-même (et évite `??`, qui ignore le null Unity).
        MoveBird bird = other.GetComponentInParent<MoveBird>();
        if (bird == null) return;

        hasWon = true;
        bird.Win(winBonusPoints);

        ParticleBurst.Play(transform.position,
            ParticleBurst.VictoryConfetti, victoryEffectPrefab);
    }
}
