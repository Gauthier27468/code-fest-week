using UnityEngine;

public class HoopScore : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    [Tooltip("Nombre de points bonus attribués lors du franchissement de l'anneau.")]
    public int bonusPoints = 100;

    private bool isCollected = false;

    public void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        if (other.CompareTag("Player") || other.GetComponentInParent<MoveBird>() != null)
        {
            isCollected = true;
            MoveBird.AddBonusScore(bonusPoints);
            Debug.Log($"[HoopScore] Anneau franchi ! +{bonusPoints} pts. Nouveau score : {MoveBird.CurrentScore}");
        }
    }
}
