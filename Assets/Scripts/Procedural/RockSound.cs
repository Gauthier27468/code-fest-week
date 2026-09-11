using UnityEngine;

public class RockSound : MonoBehaviour
{

    AudioSource audioSource;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            // Play rock sound effect
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.Play();
            }
        }
    }
}
