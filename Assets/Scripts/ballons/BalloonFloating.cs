using UnityEngine;

/// <summary>
/// Script gérant le comportement de flottaison et d'ascension aléatoire pour les montgolfières / ballons.
/// Permet d'obtenir un mouvement fluide, naturel et désynchronisé entre tous les ballons de la scène.
/// </summary>
public class BalloonFloating : MonoBehaviour
{
    [Header("--- Flottaison Verticale (Bobbing) ---")]
    [Tooltip("Active le balancement de haut en bas.")]
    [SerializeField] private bool enableBobbing = true;

    [Tooltip("Amplitude du mouvement vertical (en mètres).")]
    [Range(0.1f, 5f)]
    [SerializeField] private float bobAmplitude = 0.8f;

    [Tooltip("Vitesse de l'oscillation verticale.")]
    [Range(0.1f, 3f)]
    [SerializeField] private float bobFrequency = 0.6f;

    [Header("--- Ascension Continue (Montée) ---")]
    [Tooltip("Active une montée lente et continue vers le ciel.")]
    [SerializeField] private bool enableAscent = true;

    [Tooltip("Vitesse de montée constante (mètres par seconde).")]
    [Range(0f, 5f)]
    [SerializeField] private float ascentSpeed = 0.3f;

    [Tooltip("Si activé, réinitialise la hauteur du ballon lorsqu'il dépasse une altitude maximale.")]
    [SerializeField] private bool loopAscent = false;

    [Tooltip("Altitude maximale avant réinitialisation (uniquement si Loop Ascent est activé).")]
    [SerializeField] private float maxAltitude = 100f;

    [Header("--- Dérive Horizontale (Vent / Sway) ---")]
    [Tooltip("Active une légère dérive horizontale simulant les courants d'air.")]
    [SerializeField] private bool enableDrift = true;

    [Tooltip("Amplitude de la dérive horizontale.")]
    [Range(0f, 3f)]
    [SerializeField] private float driftAmplitude = 0.5f;

    [Tooltip("Vitesse de la dérive horizontale.")]
    [Range(0.05f, 2f)]
    [SerializeField] private float driftFrequency = 0.3f;

    [Header("--- Rotation & Tangage (Wobble) ---")]
    [Tooltip("Active une légère oscillation angulaire (roulis/tangage comme une nacelle suspendue).")]
    [SerializeField] private bool enableTilt = true;

    [Tooltip("Angle d'inclinaison maximal (en degrés).")]
    [Range(0f, 15f)]
    [SerializeField] private float maxTiltAngle = 3.5f;

    [Tooltip("Vitesse d'inclinaison angulaire.")]
    [Range(0.1f, 3f)]
    [SerializeField] private float tiltSpeed = 0.8f;

    [Tooltip("Vitesse de rotation lente sur l'axe Y (lacet continu).")]
    [Range(-10f, 10f)]
    [SerializeField] private float slowYawSpeed = 1.0f;

    [Header("--- Aléatoire / Désynchronisation ---")]
    [Tooltip("Pourcentage de variation aléatoire appliqué aux vitesses et amplitudes (0 = identique, 0.5 = ±50%).")]
    [Range(0f, 0.8f)]
    [SerializeField] private float randomnessFactor = 0.35f;

    // Positions et rotations de base
    private Vector3 _basePosition;
    private Quaternion _initialRotation;

    // Décalages et graines aléatoires uniques par instance
    private float _timeOffset;
    private float _noiseSeedX;
    private float _noiseSeedZ;
    private float _actualBobAmp;
    private float _actualBobFreq;
    private float _actualAscentSpeed;
    private float _actualDriftAmp;
    private float _actualDriftFreq;
    private float _actualTiltAngle;
    private float _actualTiltSpeed;
    private float _actualYawSpeed;

    private float _accumulatedAscent = 0f;
    private float _initialY = 0f;

    private void Awake()
    {
        _basePosition = transform.position;
        _initialY = _basePosition.y;
        _initialRotation = transform.rotation;

        InitializeRandomOffsets();
    }

    /// <summary>
    /// Initialise des valeurs uniques pour ce ballon afin d'éviter tout mouvement synchronisé avec les autres.
    /// </summary>
    private void InitializeRandomOffsets()
    {
        // Déphasage temporel aléatoire
        _timeOffset = Random.Range(0f, 1000f);
        _noiseSeedX = Random.Range(0f, 1000f);
        _noiseSeedZ = Random.Range(0f, 1000f);

        // Variation aléatoire des paramètres
        _actualBobAmp = bobAmplitude * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualBobFreq = bobFrequency * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualAscentSpeed = ascentSpeed * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualDriftAmp = driftAmplitude * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualDriftFreq = driftFrequency * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualTiltAngle = maxTiltAngle * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualTiltSpeed = tiltSpeed * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        
        // Rotation lacet aléatoire (sens horaire ou anti-horaire)
        float direction = Random.value > 0.5f ? 1f : -1f;
        _actualYawSpeed = slowYawSpeed * direction * (1f + Random.Range(-randomnessFactor, randomnessFactor));
    }

    private void Update()
    {
        float time = Time.time + _timeOffset;

        // 1. Ascension continue (si activée)
        if (enableAscent)
        {
            _accumulatedAscent += _actualAscentSpeed * Time.deltaTime;

            if (loopAscent && (_initialY + _accumulatedAscent) > maxAltitude)
            {
                _accumulatedAscent = 0f;
            }
        }

        // 2. Flottaison verticale (onde sinusoïdale + harmonique)
        float verticalOffset = 0f;
        if (enableBobbing)
        {
            // Combinaison d'une onde principale et d'une onde secondaire pour un flottement plus naturel
            verticalOffset = Mathf.Sin(time * _actualBobFreq) * _actualBobAmp
                           + Mathf.Sin(time * _actualBobFreq * 0.45f) * (_actualBobAmp * 0.25f);
        }

        // 3. Dérive horizontale via Perlin Noise pour une turbulence douce et continue
        float driftX = 0f;
        float driftZ = 0f;
        if (enableDrift)
        {
            driftX = (Mathf.PerlinNoise(time * _actualDriftFreq, _noiseSeedX) - 0.5f) * 2f * _actualDriftAmp;
            driftZ = (Mathf.PerlinNoise(_noiseSeedZ, time * _actualDriftFreq) - 0.5f) * 2f * _actualDriftAmp;
        }

        // Application de la position calculée
        Vector3 targetPosition = new Vector3(
            _basePosition.x + driftX,
            _basePosition.y + _accumulatedAscent + verticalOffset,
            _basePosition.z + driftZ
        );
        transform.position = targetPosition;

        // 4. Oscillation et rotation angulaire (légère inclinaison et rotation douce)
        Quaternion targetRotation = _initialRotation;

        if (enableTilt)
        {
            float pitch = Mathf.Sin(time * _actualTiltSpeed) * _actualTiltAngle;
            float roll = Mathf.Cos(time * _actualTiltSpeed * 0.8f) * _actualTiltAngle;
            targetRotation = _initialRotation * Quaternion.Euler(pitch, 0f, roll);
        }

        if (Mathf.Abs(_actualYawSpeed) > 0.001f)
        {
            // Rotation lente et continue sur l'axe Y
            _initialRotation = Quaternion.Euler(0f, _actualYawSpeed * Time.deltaTime, 0f) * _initialRotation;
        }

        transform.rotation = targetRotation;
    }

    /// <summary>
    /// Permet de réinitialiser la position de base à la position actuelle si nécessaire.
    /// </summary>
    public void ResetBasePosition()
    {
        _basePosition = transform.position;
        _initialY = _basePosition.y;
        _accumulatedAscent = 0f;
    }
}
