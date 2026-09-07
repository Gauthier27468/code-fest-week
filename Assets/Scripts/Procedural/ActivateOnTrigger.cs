using System.Collections.Generic;
using UnityEngine;

public class ActivateOnTrigger : MonoBehaviour
{
    [Header("Targets to Activate")]
    public List<Behaviour> componentsToActivate = new List<Behaviour>();
    public List<GameObject> gameObjectsToActivate = new List<GameObject>();

    [Header("Trigger Settings")]
    public string targetTag = "Player";
    public bool triggerOnce = true;
    public bool disableAtStart = true;

    private bool hasTriggered = false;

    private void Awake()
    {
        if (disableAtStart)
        {
            foreach (Behaviour comp in componentsToActivate)
            {
                if (comp != null) comp.enabled = false;
            }

            foreach (GameObject go in gameObjectsToActivate)
            {
                if (go != null) go.SetActive(false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && hasTriggered) return;

        if (!string.IsNullOrEmpty(targetTag) && !other.CompareTag(targetTag))
        {
            return;
        }

        hasTriggered = true;
        Debug.Log("Trigger entered by: " + other.name);

        foreach (Behaviour comp in componentsToActivate)
        {
            if (comp != null) comp.enabled = true;
        }

        foreach (GameObject go in gameObjectsToActivate)
        {
            if (go != null) go.SetActive(true);
        }
    }
}