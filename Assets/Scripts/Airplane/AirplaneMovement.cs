using UnityEngine;

public class AirplaneMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 35f;
    public Vector3 direction = Vector3.forward;
    public bool isFlying = true;

    [Header("Audio")]
    public AudioClip engineSound;
    public AudioSource audioSource;
    [Range(0f, 1f)] public float volume = 0.85f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    public bool loopSound = true;

    [Header("3D Sound")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    public float minDistance = 15f;
    public float maxDistance = 250f;
    public float dopplerLevel = 1.5f;

    private void Awake()
    {
        SetupAudio();
    }

    private void OnEnable()
    {
        if (engineSound != null && audioSource != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void OnDisable()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    private void Update()
    {
        if (!isFlying) return;

        transform.Translate(direction.normalized * speed * Time.deltaTime, Space.Self);
    }

    private void OnValidate()
    {
        UpdateAudio();
    }

    private void SetupAudio()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        UpdateAudio();
    }

    private void UpdateAudio()
    {
        if (audioSource == null) return;

        audioSource.clip = engineSound;
        audioSource.volume = volume;
        audioSource.pitch = pitch;
        audioSource.loop = loopSound;
        audioSource.playOnAwake = false;

        audioSource.spatialBlend = spatialBlend;
        audioSource.minDistance = minDistance;
        audioSource.maxDistance = maxDistance;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.dopplerLevel = dopplerLevel;
    }

    public void SetSpeed(float newSpeed)
    {
        speed = Mathf.Max(0f, newSpeed);
    }
}