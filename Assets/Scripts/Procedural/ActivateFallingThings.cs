using System.Collections.Generic;
using UnityEngine;

public class ActivateFallingThings : MonoBehaviour
{

    public List<GameObject> fallingThings;


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
            Debug.Log("Player entered the trigger. Activating a random falling thing.");
            // Choose a random falling thing from the list
            int randomIndex = Random.Range(0, fallingThings.Count);
            GameObject fallingThing = fallingThings[randomIndex];
            fallingThing.GetComponent<Rigidbody>().isKinematic = false;
        }
    }
}
