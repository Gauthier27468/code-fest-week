using System.Collections.Generic;
using UnityEngine;

/// <summary>Libère un ou plusieurs objets suspendus quand le joueur entre dans la zone.</summary>
public class ActivateFallingThings : MonoBehaviour
{
    public List<GameObject> fallingThings;
    public int numberOfThingsToActivate = 1;

    private void Awake()
    {
        foreach (GameObject fallingThing in fallingThings)
        {
            fallingThing.GetComponent<Rigidbody>().isKinematic = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        for (int i = 0; i < numberOfThingsToActivate && fallingThings.Count > 0; i++)
        {
            int randomIndex = Random.Range(0, fallingThings.Count);
            fallingThings[randomIndex].GetComponent<Rigidbody>().isKinematic = false;
            fallingThings.RemoveAt(randomIndex);
        }
    }
}
