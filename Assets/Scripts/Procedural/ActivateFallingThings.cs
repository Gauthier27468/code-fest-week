using System.Collections.Generic;
using UnityEngine;

public class ActivateFallingThings : MonoBehaviour
{

    public List<GameObject> fallingThings;
    public int numberOfThingsToActivate = 1;

    void Awake()
    {
        // Set all falling things to kinematic at the start
        foreach (GameObject fallingThing in fallingThings)
        {
            fallingThing.GetComponent<Rigidbody>().isKinematic = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trigger entered by: " + other.name);
        if (other.CompareTag("Player"))
        {
            for (int i = 0; i < numberOfThingsToActivate; i++)
            {
                Debug.Log("Player entered the trigger. Activating a random falling thing.");
                // Choose a random falling thing from the list
                int randomIndex = Random.Range(0, fallingThings.Count);
                GameObject fallingThing = fallingThings[randomIndex];
                fallingThing.GetComponent<Rigidbody>().isKinematic = false;
                fallingThings.RemoveAt(randomIndex); // Remove it from the list so it doesn't get activated again
            }
        }
    }
}
