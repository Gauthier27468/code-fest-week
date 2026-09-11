using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Comportement vertical d'un ballon / d'une montgolfière.</summary>
public enum AscentMode
{
    /// <summary>Monte jusqu'à la limite puis reste sur place à cette hauteur.</summary>
    StopAtMax,
    /// <summary>Monte, marque une pause, redescend, marque une pause, et recommence.</summary>
    PingPong,
    /// <summary>Monte jusqu'en haut puis repart d'en bas.</summary>
    Loop,
    /// <summary>Reste à sa hauteur initiale.</summary>
    None
}

/// <summary>
/// Flottaison des montgolfières / ballons : montée, balancement, dérive et oscillation.
/// Chaque ballon tire ses propres variations au démarrage pour ne pas bouger en synchro avec
/// les autres, et un plafond absolu le garde dans la zone de vol (il sert d'obstacle).
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

    [Tooltip("StopAtMax : monte puis reste en haut. PingPong : monte, pause, redescend, pause... Loop : monte puis repart d'en bas. None : reste sur place.")]
    [SerializeField] private AscentMode ascentMode = AscentMode.StopAtMax;

    [Tooltip("Vitesse de montée et descente (mètres par seconde).")]
    [Range(0f, 5f)]
    [SerializeField] private float ascentSpeed = 0.3f;

    [Tooltip("Distance maximale de montée (en mètres) par rapport à la position de départ.")]
    [Range(0.1f, 15f)]
    [FormerlySerializedAs("maxAltitude")]
    [SerializeField] private float maxAscentDistance = 1.5f;

    [Tooltip("Durée de pause (en secondes) aux extrémités, en mode PingPong.")]
    [Range(0f, 10f)]
    [SerializeField] private float pauseDuration = 3f;

    [Tooltip("Altitude monde maximale, pour que le ballon ne sorte pas de la zone de vol de l'oiseau (0 = désactivé).")]
    [SerializeField] private float clampMaxWorldAltitude = 9.0f;

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
    [Tooltip("Variation aléatoire appliquée aux vitesses et amplitudes (0 = identique, 0.5 = ±50%).")]
    [Range(0f, 0.8f)]
    [SerializeField] private float randomnessFactor = 0.35f;

    private Vector3 _basePosition;
    private Quaternion _baseRotation; // tourne lentement autour de Y (lacet)

    // Réglages propres à ce ballon, tirés au sort au démarrage.
    private float _timeOffset;
    private float _noiseSeedX;
    private float _noiseSeedZ;
    private float _bobAmp;
    private float _bobFreq;
    private float _ascentSpeed;
    private float _maxAscent;
    private float _pauseDuration;
    private float _driftAmp;
    private float _driftFreq;
    private float _tiltAngle;
    private float _tiltSpeed;
    private float _yawSpeed;

    // État de la montée.
    private float _ascent;
    private bool _isAscending = true;
    private bool _isPaused;
    private float _pauseTimer;

    private void Awake()
    {
        _basePosition = transform.position;
        _baseRotation = transform.rotation;
        InitializeRandomOffsets();
    }

    /// <summary>Tire des réglages uniques pour que ce ballon ne bouge pas en synchro avec les autres.</summary>
    private void InitializeRandomOffsets()
    {
        _timeOffset = Random.Range(0f, 1000f);
        _noiseSeedX = Random.Range(0f, 1000f);
        _noiseSeedZ = Random.Range(0f, 1000f);

        _bobAmp = Vary(bobAmplitude, randomnessFactor);
        _bobFreq = Vary(bobFrequency, randomnessFactor);
        _ascentSpeed = Vary(ascentSpeed, randomnessFactor);
        _maxAscent = Vary(maxAscentDistance, randomnessFactor * 0.5f);
        _pauseDuration = Vary(pauseDuration, randomnessFactor * 0.3f);
        _driftAmp = Vary(driftAmplitude, randomnessFactor);
        _driftFreq = Vary(driftFrequency, randomnessFactor);
        _tiltAngle = Vary(maxTiltAngle, randomnessFactor);
        _tiltSpeed = Vary(tiltSpeed, randomnessFactor);

        // Lacet dans un sens ou dans l'autre.
        float direction = Random.value > 0.5f ? 1f : -1f;
        _yawSpeed = direction * Vary(slowYawSpeed, randomnessFactor);

        if (ascentMode == AscentMode.PingPong)
        {
            // Point de départ aléatoire dans le cycle.
            _ascent = Random.Range(0f, _maxAscent);
            _isAscending = Random.value > 0.5f;
            if (Random.value > 0.5f)
            {
                _isPaused = true;
                _pauseTimer = Random.Range(0.5f, _pauseDuration);
            }
        }
    }

    /// <summary>value ± variation (en proportion de value).</summary>
    private static float Vary(float value, float variation) =>
        value * (1f + Random.Range(-variation, variation));

    private void Update()
    {
        float time = Time.time + _timeOffset;

        if (enableAscent) UpdateAscent(Time.deltaTime);

        // Balancement : onde principale + harmonique lente, plus naturel qu'un simple sinus.
        float bob = 0f;
        if (enableBobbing)
        {
            bob = Mathf.Sin(time * _bobFreq) * _bobAmp
                + Mathf.Sin(time * _bobFreq * 0.45f) * (_bobAmp * 0.25f);
        }

        // Dérive : bruit de Perlin, pour une turbulence douce et continue.
        float driftX = 0f;
        float driftZ = 0f;
        if (enableDrift)
        {
            driftX = (Mathf.PerlinNoise(time * _driftFreq, _noiseSeedX) - 0.5f) * 2f * _driftAmp;
            driftZ = (Mathf.PerlinNoise(_noiseSeedZ, time * _driftFreq) - 0.5f) * 2f * _driftAmp;
        }

        float y = _basePosition.y + _ascent + bob;
        if (clampMaxWorldAltitude > 0f) y = Mathf.Min(y, clampMaxWorldAltitude);

        transform.position = new Vector3(_basePosition.x + driftX, y, _basePosition.z + driftZ);

        // Lacet continu, puis oscillation de la nacelle par-dessus.
        _baseRotation = Quaternion.Euler(0f, _yawSpeed * Time.deltaTime, 0f) * _baseRotation;

        Quaternion rotation = _baseRotation;
        if (enableTilt)
        {
            float pitch = Mathf.Sin(time * _tiltSpeed) * _tiltAngle;
            float roll = Mathf.Cos(time * _tiltSpeed * 0.8f) * _tiltAngle;
            rotation *= Quaternion.Euler(pitch, 0f, roll);
        }
        transform.rotation = rotation;
    }

    private void UpdateAscent(float dt)
    {
        switch (ascentMode)
        {
            case AscentMode.StopAtMax:
                _ascent = Mathf.Min(_ascent + _ascentSpeed * dt, _maxAscent);
                break;

            case AscentMode.PingPong:
                if (_isPaused)
                {
                    _pauseTimer -= dt;
                    if (_pauseTimer <= 0f)
                    {
                        _isPaused = false;
                        _isAscending = !_isAscending;
                    }
                }
                else
                {
                    _ascent += (_isAscending ? _ascentSpeed : -_ascentSpeed) * dt;
                    if (_ascent >= _maxAscent || _ascent <= 0f)
                    {
                        _ascent = Mathf.Clamp(_ascent, 0f, _maxAscent);
                        _isPaused = true;
                        _pauseTimer = _pauseDuration;
                    }
                }
                break;

            case AscentMode.Loop:
                _ascent += _ascentSpeed * dt;
                if (_ascent >= _maxAscent) _ascent = 0f;
                break;
        }
    }
}
