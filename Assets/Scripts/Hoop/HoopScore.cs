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

    public void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Scored!");
            // You can add scoring logic here, such as updating the score UI or triggering an event.
        }
    }
}
