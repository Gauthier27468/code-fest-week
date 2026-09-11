using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Mode de comportement vertical pour les ballons / montgolfières.
/// </summary>
public enum AscentMode
{
    [Tooltip("Monte jusqu'à la limite max puis s'arrête et reste sur place à cette hauteur.")]
    StopAtMax,

    [Tooltip("Monte jusqu'en haut, reste sur place pendant une pause, redescend, fait une pause en bas, et recommence.")]
    PingPong,

    [Tooltip("Monte jusqu'en haut puis se réinitialise en bas.")]
    Loop,

    [Tooltip("Aucune montée : reste directement sur place à sa position initiale.")]
    None
}

/// <summary>
/// Script gérant le comportement de flottaison et d'ascension pour les montgolfières / ballons.
/// Permet d'obtenir un mouvement fluide, naturel et désynchronisé entre tous les ballons de la scène,
/// tout en garantissant qu'ils restent dans la zone de jeu pour servir d'obstacles.
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

    [Header("--- Déplacement Vertical / Montée ---")]
    [Tooltip("Active une montée ou un déplacement vertical.")]
    [SerializeField] private bool enableAscent = true;

    [Tooltip("Comportement d'ascension :\n- StopAtMax : monte jusqu'à maxAscentDistance et reste définitivement sur place.\n- PingPong : monte, reste sur place un moment (pause), redescend, fait une pause, etc.\n- Loop : monte puis boucle depuis le bas.\n- None : reste sur place dès le début.")]
    [SerializeField] private AscentMode ascentMode = AscentMode.StopAtMax;

    [Tooltip("Vitesse de montée et descente (mètres par seconde).")]
    [Range(0f, 5f)]
    [SerializeField] private float ascentSpeed = 0.3f;

    [Tooltip("Distance maximale de montée (en mètres) par rapport à la position de départ.")]
    [Range(0.1f, 15f)]
    [FormerlySerializedAs("maxAltitude")]
    [SerializeField] private float maxAscentDistance = 1.5f;

    [Tooltip("Durée de pause (en secondes) pendant laquelle le ballon reste immobile sur place aux extrémités (en mode PingPong).")]
    [Range(0f, 10f)]
    [SerializeField] private float pauseDuration = 3f;

    [Tooltip("Plafond d'altitude maximale absolue dans le monde pour éviter que le ballon ne sorte de la zone de vol de l'oiseau (0 = désactivé).")]
    [SerializeField] private float clampMaxWorldAltitude = 9.0f;

    [FormerlySerializedAs("loopAscent")]
    [HideInInspector]
    [SerializeField] private bool _legacyLoopAscent = false;

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
    private float _actualMaxAscentDistance;
    private float _actualPauseDuration;
    private float _actualDriftAmp;
    private float _actualDriftFreq;
    private float _actualTiltAngle;
    private float _actualTiltSpeed;
    private float _actualYawSpeed;

    private float _accumulatedAscent = 0f;
    private float _initialY = 0f;
    private bool _isAscending = true;
    private bool _isPaused = false;
    private float _pauseTimer = 0f;

    public AscentMode CurrentAscentMode { get => ascentMode; set => ascentMode = value; }
    public float MaxAscentDistance { get => maxAscentDistance; set => maxAscentDistance = value; }
    public float PauseDuration { get => pauseDuration; set => pauseDuration = value; }

    private void Awake()
    {
        _basePosition = transform.position;
        _initialY = _basePosition.y;
        _initialRotation = transform.rotation;

        if (_legacyLoopAscent && ascentMode == AscentMode.StopAtMax)
        {
            ascentMode = AscentMode.Loop;
        }

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
        _actualMaxAscentDistance = maxAscentDistance * (1f + Random.Range(-randomnessFactor * 0.5f, randomnessFactor * 0.5f));
        _actualPauseDuration = pauseDuration * (1f + Random.Range(-randomnessFactor * 0.3f, randomnessFactor * 0.3f));
        _actualDriftAmp = driftAmplitude * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualDriftFreq = driftFrequency * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualTiltAngle = maxTiltAngle * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        _actualTiltSpeed = tiltSpeed * (1f + Random.Range(-randomnessFactor, randomnessFactor));
        
        // Rotation lacet aléatoire (sens horaire ou anti-horaire)
        float direction = Random.value > 0.5f ? 1f : -1f;
        _actualYawSpeed = slowYawSpeed * direction * (1f + Random.Range(-randomnessFactor, randomnessFactor));

        if (ascentMode == AscentMode.PingPong)
        {
            // Déphasage initial pour désynchroniser les ballons
            _accumulatedAscent = Random.Range(0f, _actualMaxAscentDistance);
            _isAscending = Random.value > 0.5f;
            if (Random.value > 0.5f)
            {
                _isPaused = true;
                _pauseTimer = Random.Range(0.5f, _actualPauseDuration);
            }
        }
    }

    private void Update()
    {
        float time = Time.time + _timeOffset;

        // 1. Déplacement vertical / Ascension
        if (enableAscent && ascentMode != AscentMode.None)
        {
            switch (ascentMode)
            {
                case AscentMode.StopAtMax:
                    // Monte jusqu'à la limite puis s'arrête et reste définitivement sur place à cette hauteur
                    if (_accumulatedAscent < _actualMaxAscentDistance)
                    {
                        _accumulatedAscent += _actualAscentSpeed * Time.deltaTime;
                        if (_accumulatedAscent >= _actualMaxAscentDistance)
                        {
                            _accumulatedAscent = _actualMaxAscentDistance;
                        }
                    }
                    break;

                case AscentMode.PingPong:
                    // Monte, fait une pause sur place (obstacle temporaire), redescend, fait une pause en bas, et recommence
                    if (_isPaused)
                    {
                        _pauseTimer -= Time.deltaTime;
                        if (_pauseTimer <= 0f)
                        {
                            _isPaused = false;
                            _isAscending = !_isAscending;
                        }
                    }
                    else
                    {
                        if (_isAscending)
                        {
                            _accumulatedAscent += _actualAscentSpeed * Time.deltaTime;
                            if (_accumulatedAscent >= _actualMaxAscentDistance)
                            {
                                _accumulatedAscent = _actualMaxAscentDistance;
                                _isPaused = true;
                                _pauseTimer = _actualPauseDuration;
                            }
                        }
                        else
                        {
                            _accumulatedAscent -= _actualAscentSpeed * Time.deltaTime;
                            if (_accumulatedAscent <= 0f)
                            {
                                _accumulatedAscent = 0f;
                                _isPaused = true;
                                _pauseTimer = _actualPauseDuration;
                            }
                        }
                    }
                    break;

                case AscentMode.Loop:
                    // Monte et se réinitialise en bas une fois en haut
                    _accumulatedAscent += _actualAscentSpeed * Time.deltaTime;
                    if (_accumulatedAscent >= _actualMaxAscentDistance)
                    {
                        _accumulatedAscent = 0f;
                    }
                    break;
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

        // 4. Calcul de la hauteur avec limitation de plafond absolue (pour ne pas sortir de la zone de vol de l'oiseau)
        float targetY = _basePosition.y + _accumulatedAscent + verticalOffset;
        if (clampMaxWorldAltitude > 0f && targetY > clampMaxWorldAltitude)
        {
            targetY = clampMaxWorldAltitude;
        }

        // Application de la position calculée
        Vector3 targetPosition = new Vector3(
            _basePosition.x + driftX,
            targetY,
            _basePosition.z + driftZ
        );
        transform.position = targetPosition;

        // 5. Oscillation et rotation angulaire (légère inclinaison et rotation douce)
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
        _isAscending = true;
        _isPaused = false;
        _pauseTimer = 0f;
    }
}
