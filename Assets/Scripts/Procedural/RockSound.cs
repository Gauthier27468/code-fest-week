using UnityEngine;

/// <summary>Joue le son de l'AudioSource quand le rocher touche le sol (tag "Ground").</summary>
public class RockSound : MonoBehaviour
{
    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (audioSource != null && collision.gameObject.CompareTag("Ground"))
        {
            audioSource.Play();
        }
    }
}
