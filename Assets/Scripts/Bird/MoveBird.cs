using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using KiBird.FX;
using KiBird.MainMenu;

/// <summary>
/// Contrôleur de l'oiseau : déplacement (Kinect, sinon clavier/manette), inclinaison, limites
/// de vol, score, sons, mort et victoire.
/// Tant que l'oiseau vole, c'est le Transform qui le pilote ; à la mort, le Rigidbody reprend
/// la main pour une chute physique jusqu'au sol.
/// </summary>
public class MoveBird : MonoBehaviour
{
    [Header("Movement")]
    public bool autoMoveForward = true;
    public float forwardSpeed = 8f;
    public float horizontalSpeed = 7f;
    public float verticalSpeed = 5f;
    public float boostMultiplier = 1.75f;

    [Header("Banking")]
    public bool enableBanking = true;
    public float bankAngle = 25f;
    public float pitchAngle = 15f;
    public float rotationSpeed = 5f;

    [Header("Camera Stabilization")]
    [Tooltip("Référence vers la caméra (auto-détectée dans les enfants si laissée vide).")]
    public Camera birdCamera;

    [Tooltip("Garde la caméra toujours horizontale et stable : elle ne s'incline pas quand l'oiseau vire ou tangue.")]
    public bool keepCameraHorizontal = true;

    [Header("Kinect")]
    [Tooltip("Laisser vide : l'écouteur Kinect est trouvé tout seul au démarrage. " +
             "Sans joueur détecté (bridge coupé, personne dans la zone), le clavier reprend la main.")]
    public KinectInputSource kinectSource;

    [Tooltip("Décocher pour forcer le clavier, même si la Kinect envoie des données.")]
    public bool useKinectWhenAvailable = true;

    [Header("Glide & Dive Tuning")]
    [Tooltip("Taux de chute naturel en vol plané (bras à l'horizontale / neutre clavier). -0.08 correspond au réglage Kinect.")]
    public float glideSink = -0.08f;

    [Tooltip("Taux de chute en piqué / chute (bras le long du corps / touche descendre). -0.55 correspond au réglage Kinect.")]
    public float diveSink = -0.55f;

    [Tooltip("Durée de l'impulsion de montée après appui sur la touche saut/battement (en s).")]
    public float keyboardFlapDuration = 0.4f;

    [Header("Animation")]
    public Animator animator;
    [Range(0.1f, 10f)] public float animationSpeed = 1f;
    public bool syncAnimationWithSpeed = true;

    [Header("Animation Thresholds")]
    [Tooltip("Seuil d'inclinaison/direction pour déclencher l'animation de vol battu (virage).")]
    public float turnAnimationThreshold = 0.08f;

    [Tooltip("Seuil de montée pour déclencher l'animation de vol battu (montée).")]
    public float climbAnimationThreshold = 0.05f;

    [Tooltip("Seuil de descente pour déclencher l'animation de piqué/plongeon (dive).")]
    public float diveAnimationThreshold = -0.25f;

    [Header("Boundary Clamping (Montagnes & Altitude)")]
    [Tooltip("Active le confinement global de l'oiseau (limites latérales et altitude).")]
    public bool enableClamping = true;

    [Tooltip("Active le confinement horizontal (axe X). Décocher si les limites latérales sont gérées par les colliders solides des falaises/rochers.")]
    public bool enableHorizontalClamping = true;

    [Tooltip("Limite X gauche (montagne gauche).")]
    public float defaultMinX = BlockBounds.StandardDefaultMinX;

    [Tooltip("Limite X droite (montagne droite).")]
    public float defaultMaxX = BlockBounds.StandardDefaultMaxX;

    [Tooltip("Altitude maximale. Au plafond, l'oiseau replane automatiquement.")]
    public float defaultMaxHeight = BlockBounds.StandardDefaultMaxHeight;

    [Tooltip("Altitude minimale (sol/eau).")]
    public float defaultMinHeight = BlockBounds.StandardDefaultMinHeight;

    [Tooltip("Adapte les limites de vol au bloc traversé (BlockBounds). Sans BlockBounds sur le bloc, les limites par défaut ci-dessus s'appliquent.")]
    public bool useBlockBounds = true;

    [Tooltip("Durée de planage forcé après avoir touché le plafond (l'oiseau ne peut plus remonter immédiatement).")]
    public float ceilingRecoveryDuration = 0.6f;

    [Tooltip("Vitesse d'interpolation des limites lors du passage d'un bloc à l'autre (si useBlockBounds est activé).")]
    public float boundsTransitionSpeed = 8f;

    [Header("Score & Survival (Game Over)")]
    [Tooltip("Points gagnés par mètre parcouru le long de l'axe Z.")]
    public float scorePerMeter = 1f;

    [Header("UI Menu & Sound Effect")]
    [SerializeField] private Text scoreText;
    [SerializeField] private AudioSource birdSource;
    [Tooltip("AudioSource dédié à la musique de fond (créé au démarrage si vide).")]
    [SerializeField] private AudioSource musicAudioSource;
    [Tooltip("AudioSource dédié aux battements d'ailes (créé au démarrage si vide, pour ne pas couper la musique).")]
    [SerializeField] private AudioSource flapAudioSource;

    [Header("Musique de fond")]
    [SerializeField] private AudioClip mainMusic;
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.35f;

    [Header("Bruitages")]
    [SerializeField] private AudioClip dieSound;
    [Range(0f, 1f)]
    [SerializeField] private float dieSoundVolume = 1.0f;
    [Tooltip("Battement d'ailes, joué par l'Animation Event PlayFlapSound de l'animation de vol.")]
    [SerializeField] private AudioClip flapSound;
    [Range(0f, 1f)]
    [SerializeField] private float flapVolume = 0.8f;
    [Tooltip("Légère variation aléatoire de la hauteur du son à chaque battement, pour un rendu naturel.")]
    [SerializeField] private bool randomizeFlapPitch = true;

    [Header("Effets Visuels & Mort")]
    [Tooltip("Préfab de plumes joué à la mort (optionnel : généré par ParticleBurst si vide).")]
    [SerializeField] private ParticleSystem featherParticlePrefab;

    // ------------------------------------------------------------------ état global de la partie

    /// <summary>Score actuel du joueur (lu par l'UI de fin de partie).</summary>
    public static int CurrentScore;

    /// <summary>Temps de survie actuel en secondes.</summary>
    public static float SurvivalTime;

    public static bool IsDead;
    public static bool IsWon;

    public static event System.Action OnBirdDied;
    public static event System.Action OnBirdWon;

    private static int bonusScore;

    /// <summary>Ajoute des points bonus (anneaux, arrivée au nid).</summary>
    public static void AddBonusScore(int points)
    {
        bonusScore += points;
        CurrentScore += points;
    }

    // ------------------------------------------------------------------ état interne

    private static readonly int FlyingHash = Animator.StringToHash("Flying");
    private static readonly int DiveHash = Animator.StringToHash("Dive");

    /// <summary>Scores 0..1999 pré-convertis en string : zéro allocation à chaque point gagné.</summary>
    private static readonly string[] ScoreStringCache = BuildScoreStringCache();

    private Rigidbody rb;
    private float startZPos;
    private int displayedScore = -1;

    private Quaternion initialRotation;
    private Quaternion initialCameraWorldRotation;
    private Vector3 cameraWorldOffset;
    private float currentRoll;
    private float currentPitch;
    private float keyboardFlapTimer;
    private float ceilingLockoutTimer;

    // Limites de vol courantes, interpolées vers celles du bloc traversé.
    private float currentMinX;
    private float currentMaxX;
    private float currentMaxHeight;
    private float currentMinHeight;

    // Chute après la mort.
    private bool isGrounded;
    private float deathTime;
    private Collider killedByCollider;
    private bool isGroundImpact;

    /// <summary>
    /// Écouteur à interroger : celui câblé dans l'inspecteur, sinon celui créé automatiquement.
    /// Résolu à chaque accès : le bootstrap Kinect tourne après les Awake de la scène.
    /// </summary>
    private KinectInputSource ActiveKinectSource =>
        kinectSource != null ? kinectSource : KinectInputSource.Instance;

    /// <summary>La Kinect pilote l'oiseau : joueur verrouillé et paquets frais.</summary>
    private bool IsKinectDriving
    {
        get
        {
            if (!useKinectWhenAvailable) return false;
            var source = ActiveKinectSource;
            return source != null && source.HasPlayer;
        }
    }

    private float SpeedSqr => rb.linearVelocity.sqrMagnitude;

    // ------------------------------------------------------------------ cycle de vie Unity

    private void Awake()
    {
        // Les statiques survivent au rechargement de scène : on repart de zéro à chaque partie.
        CurrentScore = 0;
        SurvivalTime = 0f;
        IsDead = false;
        IsWon = false;
        bonusScore = 0;
        startZPos = transform.position.z;

        SetupAudioSources();
        SetupRigidbody();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetBool(FlyingHash, false);
            animator.SetBool(DiveHash, false);
        }
        initialRotation = transform.localRotation;

        if (birdCamera == null) birdCamera = GetComponentInChildren<Camera>();
        if (birdCamera != null)
        {
            initialCameraWorldRotation = birdCamera.transform.rotation;
            cameraWorldOffset = birdCamera.transform.position - transform.position;
        }

        currentMinX = defaultMinX;
        currentMaxX = defaultMaxX;
        currentMaxHeight = defaultMaxHeight;
        currentMinHeight = defaultMinHeight;

        UpdateAnimationSpeed();
        RefreshScoreLabel();
    }

    private void SetupAudioSources()
    {
        if (birdSource == null)
        {
            birdSource = GetComponent<AudioSource>();
            if (birdSource == null) birdSource = gameObject.AddComponent<AudioSource>();
        }

        // Sources séparées : la musique et les battements d'ailes ont leur propre volume et ne
        // se coupent pas entre eux.
        if (musicAudioSource == null)
        {
            musicAudioSource = gameObject.AddComponent<AudioSource>();
            musicAudioSource.playOnAwake = false;
        }
        ApplyMusicVolume();

        if (flapAudioSource == null)
        {
            flapAudioSource = gameObject.AddComponent<AudioSource>();
            flapAudioSource.playOnAwake = false;
            flapAudioSource.spatialBlend = birdSource.spatialBlend;
        }
    }

    /// <summary>Rigidbody dynamique, nécessaire pour détecter les collisions avec le décor.</summary>
    private void SetupRigidbody()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = false;
        // ContinuousSpeculative plutôt que Continuous : tant que l'oiseau est vivant il est
        // téléporté par transform.Translate() avec une vélocité remise à zéro, or la CCD balayée
        // de PhysX se base sur la vélocité. Continuous ne détecterait donc rien tout en payant des
        // sweeps contre les MeshColliders du décor. Les contacts spéculatifs, eux, fonctionnent
        // sur un corps téléporté et coûtent une fraction du prix.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        // Interpolation désactivée tant que le Transform pilote l'oiseau : l'interpolation reprend
        // la main sur le Transform à chaque frame de rendu et le replace sur la pose du dernier pas
        // physique (50 Hz). Les déplacements écrits dans Update() entre deux pas sont alors écrasés,
        // ce qui produit des retours en arrière visibles et une perte de vitesse d'autant plus forte
        // que le framerate est élevé. Elle est réactivée dans Die(), où la physique pilote la chute.
        rb.interpolation = RigidbodyInterpolation.None;
    }

    private void Update()
    {
        if (IsDead || IsWon) return;

        SurvivalTime += Time.deltaTime;
        float distance = Mathf.Max(0f, transform.position.z - startZPos);
        CurrentScore = Mathf.FloorToInt(distance * scorePerMeter) + bonusScore;

        // Debug : K tue l'oiseau instantanément.
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            Die();
            return;
        }
        HandleTuningKeys();

        UpdateBoundaries();
        Vector3 rawInput = GetInput();
        Vector3 input = EnforceBoundaryConstraints(rawInput);
        Move(input);
        Bank(input);
        // L'animation suit les commandes du joueur, même quand le plafond force le planage.
        UpdateAnimation(rawInput);

        RefreshScoreLabel();
    }

    /// <summary>
    /// Neutralise la vélocité résiduelle au rythme de la physique : tant que l'oiseau vole,
    /// c'est le Transform qui le pilote et PhysX ne doit rien ajouter par-dessus.
    /// </summary>
    private void FixedUpdate()
    {
        if (IsDead || IsWon) return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Maintient la caméra horizontale derrière l'oiseau. Exécuté après les Update pour annuler
    /// le roulis et le tangage qui feraient pencher l'horizon.
    /// </summary>
    private void LateUpdate()
    {
        if (birdCamera == null || IsDead || !keepCameraHorizontal) return;

        birdCamera.transform.rotation = initialCameraWorldRotation;
        birdCamera.transform.position = transform.position + cameraWorldOffset;
    }

    private void OnValidate()
    {
        UpdateAnimationSpeed();
        ApplyMusicVolume();
    }

    // ------------------------------------------------------------------ entrées

    private Vector3 GetInput()
    {
        // Kinect prioritaire tant qu'un joueur est verrouillé ; sinon, bascule sans transition sur
        // le clavier (indispensable pour les tests et pour les animateurs pendant la JPO).
        if (IsKinectDriving) return ActiveKinectSource.Input;

        float x = 0f;
        float y = glideSink; // Vol plané par défaut, identique à la Kinect bras à l'horizontale.
        float z = 0f;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // Virage : Q / A / Flèche Gauche, ou D / Flèche Droite.
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed || keyboard.qKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;

            // Battement / Montée : Espace ou Flèche Haut. Chute / Piqué : Ctrl, C ou Flèche Bas.
            bool flapHeld = keyboard.spaceKey.isPressed || keyboard.upArrowKey.isPressed;
            bool divePressed = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed || keyboard.downArrowKey.isPressed;

            if (flapHeld)
            {
                keyboardFlapTimer = keyboardFlapDuration;
                y = 1f;
            }
            else if (keyboardFlapTimer > 0f)
            {
                // Impulsion qui décroît après le relâchement (comme lift_impulse côté bridge).
                keyboardFlapTimer -= Time.deltaTime;
                y = Mathf.Clamp01(keyboardFlapTimer / keyboardFlapDuration);
            }
            else if (divePressed)
            {
                y = diveSink;
            }

            // Vitesse : Z / W pour accélérer, S pour ralentir.
            if (keyboard.wKey.isPressed || keyboard.zKey.isPressed) z += 1f;
            if (keyboard.sKey.isPressed) z -= 1f;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            x += stick.x;
            if (Mathf.Abs(stick.y) > 0.1f)
            {
                y = stick.y > 0 ? stick.y : Mathf.Lerp(glideSink, diveSink, -stick.y);
            }
        }

        return new Vector3(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
    }

    /// <summary>Boost : Shift ou gâchette droite de la manette.</summary>
    private bool IsBoosting()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.rightTrigger.ReadValue() > 0.2f;
    }

    /// <summary>Réglages à chaud : +/- pour la vitesse d'avance, P/O (ou PgUp/PgDn) pour l'animation.</summary>
    private void HandleTuningKeys()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.numpadPlusKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame) forwardSpeed += 1f;
        if (keyboard.numpadMinusKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame) forwardSpeed = Mathf.Max(1f, forwardSpeed - 1f);

        if (keyboard.pageUpKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame) SetAnimationSpeed(animationSpeed + 0.25f);
        if (keyboard.pageDownKey.wasPressedThisFrame || keyboard.oKey.wasPressedThisFrame) SetAnimationSpeed(animationSpeed - 0.25f);
    }

    // ------------------------------------------------------------------ déplacement

    private void Move(Vector3 input)
    {
        float speedMod = IsBoosting() ? boostMultiplier : 1f;
        // En avance automatique, input.z module la vitesse de ±40 % au lieu de la piloter seul.
        float fwd = autoMoveForward ? forwardSpeed + input.z * forwardSpeed * 0.4f : input.z * forwardSpeed;

        Vector3 move = new Vector3(
            input.x * horizontalSpeed,
            input.y * verticalSpeed,
            fwd) * (speedMod * Time.deltaTime);

        transform.Translate(move, Space.World);

        if (enableClamping)
        {
            Vector3 pos = transform.position;
            if (enableHorizontalClamping)
            {
                pos.x = Mathf.Clamp(pos.x, currentMinX, currentMaxX);
            }
            pos.y = Mathf.Clamp(pos.y, currentMinHeight, currentMaxHeight);
            transform.position = pos;
        }
    }

    /// <summary>Incline l'oiseau dans les virages (roulis) et en montée/descente (tangage).</summary>
    private void Bank(Vector3 input)
    {
        if (!enableBanking) return;

        float t = Time.deltaTime * rotationSpeed;
        currentRoll = Mathf.Lerp(currentRoll, -input.x * bankAngle, t);
        currentPitch = Mathf.Lerp(currentPitch, -input.y * pitchAngle, t);

        transform.localRotation = initialRotation * Quaternion.Euler(currentPitch, 0f, currentRoll);
    }

    /// <summary>Limites de vol : celles du bloc traversé si useBlockBounds, sinon celles par défaut.</summary>
    private void UpdateBoundaries()
    {
        if (!enableClamping) return;

        if (!useBlockBounds)
        {
            currentMinX = defaultMinX;
            currentMaxX = defaultMaxX;
            currentMaxHeight = defaultMaxHeight;
            currentMinHeight = defaultMinHeight;
            return;
        }

        // Interpolation : pas de saut brutal des limites à la frontière entre deux blocs.
        BlockBounds block = BlockBounds.GetBoundsAtZ(transform.position.z);
        float t = Time.deltaTime * boundsTransitionSpeed;
        currentMinX = Mathf.Lerp(currentMinX, block != null ? block.minX : defaultMinX, t);
        currentMaxX = Mathf.Lerp(currentMaxX, block != null ? block.maxX : defaultMaxX, t);
        currentMaxHeight = Mathf.Lerp(currentMaxHeight, block != null ? block.maxHeight : defaultMaxHeight, t);
        currentMinHeight = Mathf.Lerp(currentMinHeight, block != null ? block.minHeight : defaultMinHeight, t);
    }

    /// <summary>Corrige l'input aux limites : planage forcé au plafond, mort au sol, murs latéraux.</summary>
    private Vector3 EnforceBoundaryConstraints(Vector3 input)
    {
        if (!enableClamping) return input;

        // Plafond : l'oiseau ne peut plus monter pendant ceilingRecoveryDuration.
        if (transform.position.y >= currentMaxHeight - 0.05f)
        {
            ceilingLockoutTimer = ceilingRecoveryDuration;
        }
        if (ceilingLockoutTimer > 0f)
        {
            ceilingLockoutTimer -= Time.deltaTime;
            // Au plus glideSink : le piqué (diveSink) reste possible.
            input.y = Mathf.Min(input.y, glideSink);
        }

        // Plancher : piquer au niveau du sol ou de l'eau tue l'oiseau.
        if (transform.position.y <= currentMinHeight + 0.05f && input.y < 0f)
        {
            input.y = 0f;
            Die();
        }

        // Murs latéraux : empêche de braquer davantage dans la paroi.
        if (enableHorizontalClamping)
        {
            if (transform.position.x <= currentMinX + 0.05f && input.x < 0f) input.x = 0f;
            else if (transform.position.x >= currentMaxX - 0.05f && input.x > 0f) input.x = 0f;
        }

        return input;
    }

    // ------------------------------------------------------------------ animation & son

    private void UpdateAnimation(Vector3 input)
    {
        if (animator == null) return;

        // Piqué -> animation "dive" ; virage ou montée -> battement d'ailes ("Flying") ;
        // sinon -> planage (aucun des deux).
        bool isDiving = input.y < diveAnimationThreshold;
        bool isTurning = Mathf.Abs(input.x) > turnAnimationThreshold;
        bool isClimbing = input.y > climbAnimationThreshold;

        animator.SetBool(FlyingHash, !isDiving && (isTurning || isClimbing));
        animator.SetBool(DiveHash, isDiving);

        bool speedUp = syncAnimationWithSpeed && forwardSpeed > 0.01f && IsBoosting();
        animator.speed = animationSpeed * (speedUp ? boostMultiplier : 1f);
    }

    private void SetAnimationSpeed(float newSpeed)
    {
        animationSpeed = Mathf.Clamp(newSpeed, 0.1f, 10f);
        UpdateAnimationSpeed();
    }

    private void UpdateAnimationSpeed()
    {
        if (animator != null) animator.speed = animationSpeed;
    }

    /// <summary>Appelé par l'Animation Event de l'animation de vol, calé sur le battement.</summary>
    public void PlayFlapSound()
    {
        if (IsDead || IsWon || flapSound == null) return;

        flapAudioSource.pitch = randomizeFlapPitch ? Random.Range(0.93f, 1.07f) : 1f;
        flapAudioSource.PlayOneShot(flapSound, flapVolume);
    }

    /// <summary>Lance la musique de fond (appelé par le menu au démarrage de la partie).</summary>
    public void PlayMainMusic()
    {
        if (mainMusic == null) return;

        musicAudioSource.clip = mainMusic;
        musicAudioSource.loop = true;
        musicAudioSource.spatialBlend = 0f;
        musicAudioSource.volume = musicVolume;
        musicAudioSource.Play();
    }

    private void ApplyMusicVolume()
    {
        if (musicAudioSource != null) musicAudioSource.volume = musicVolume;
    }

    /// <summary>
    /// Réécrit le score uniquement quand il change : écrire dans un Text à chaque frame alloue
    /// une string et force un rebuild complet du Canvas.
    /// </summary>
    private void RefreshScoreLabel()
    {
        if (scoreText == null || displayedScore == CurrentScore) return;

        displayedScore = CurrentScore;
        scoreText.text = CurrentScore >= 0 && CurrentScore < ScoreStringCache.Length
            ? ScoreStringCache[CurrentScore]
            : CurrentScore.ToString();
    }

    private static string[] BuildScoreStringCache()
    {
        var cache = new string[2000];
        for (int i = 0; i < cache.Length; i++) cache[i] = i.ToString();
        return cache;
    }

    // ------------------------------------------------------------------ fin de partie

    /// <summary>Victoire : l'oiseau s'est posé dans le nid du bloc de fin.</summary>
    public void Win(int finishBonus = 500)
    {
        if (IsDead || IsWon) return;

        IsWon = true;
        StopFlight();
        FreezeRigidbody();

        if (finishBonus > 0) AddBonusScore(finishBonus);

        Debug.Log($"[MoveBird] Victoire ! Score final : {CurrentScore} pts | Survie : {SurvivalTime:F1}s");

        // Invoqué AVANT AddScore : GameOverUI doit comparer le score de ce vol au meilleur
        // score des vols précédents, pas au classement déjà mis à jour avec ce même score.
        OnBirdWon?.Invoke();
        ScoreManager.AddScore(CurrentScore);
    }

    /// <summary>
    /// Mort : l'oiseau lâche des plumes, chute sous l'effet de la gravité jusqu'au sol, et
    /// l'écran de Game Over est déclenché.
    /// </summary>
    public void Die(Collision collision = null)
    {
        if (IsDead) return;

        IsDead = true;
        StopFlight();
        if (dieSound != null) birdSource.PlayOneShot(dieSound, dieSoundVolume);

        deathTime = Time.time;
        ParticleBurst.Play(transform.position, ParticleBurst.Feathers,
            featherParticlePrefab != null ? featherParticlePrefab.gameObject : null);

        // Animator coupé : c'est la physique qui anime la chute.
        if (animator != null) animator.enabled = false;

        killedByCollider = collision != null ? collision.collider : null;
        isGroundImpact = IsGroundImpact(collision);

        StartTumble(collision);
        StartCoroutine(MonitorGroundLanding());

        Debug.Log($"[MoveBird] L'oiseau est mort ! Score final : {CurrentScore} pts | Survie : {SurvivalTime:F1}s");

        // Invoqué AVANT AddScore, pour la même raison que dans Win().
        OnBirdDied?.Invoke();

        // Caméra détachée : elle suit la chute sans vriller avec l'oiseau.
        if (birdCamera != null)
        {
            birdCamera.transform.SetParent(null, true);
            StartCoroutine(FollowFallingBird(birdCamera.transform));
        }

        ScoreManager.AddScore(CurrentScore);
    }

    /// <summary>Coupe les sons de vol, les vitesses et les animations (mort comme victoire).</summary>
    private void StopFlight()
    {
        musicAudioSource.Stop();
        flapAudioSource.Stop();
        birdSource.Stop();

        forwardSpeed = 0f;
        horizontalSpeed = 0f;
        verticalSpeed = 0f;

        if (animator != null)
        {
            animator.SetBool(FlyingHash, false);
            animator.SetBool(DiveHash, false);
        }
    }

    /// <summary>L'impact mortel a-t-il eu lieu au sol (ou dans l'eau) plutôt qu'en altitude ?</summary>
    private bool IsGroundImpact(Collision collision)
    {
        if (collision == null || collision.collider is TerrainCollider) return true;

        string hitName = collision.gameObject.name.ToLowerInvariant();
        return hitName.Contains("terrain") || hitName.Contains("water")
            || transform.position.y <= currentMinHeight + 0.8f;
    }

    /// <summary>Rend l'oiseau à la physique et le projette, pour qu'il culbute et tombe.</summary>
    private void StartTumble(Collision collision)
    {
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.None;
        // La physique pilote désormais le Transform : l'interpolation lisse la chute entre les
        // pas à 50 Hz au lieu d'entrer en conflit avec le script.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 1.0f;

        // Friction élevée : l'oiseau s'arrête au sol au lieu de glisser.
        Collider birdCol = GetComponent<Collider>();
        if (birdCol != null)
        {
            birdCol.material = new PhysicsMaterial("DeadBirdTumble")
            {
                dynamicFriction = 0.7f,
                staticFriction = 0.9f,
                bounciness = 0.15f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        if (collision != null && collision.contactCount > 0)
        {
            // Rebond à l'opposé de la surface touchée, pour éjecter l'oiseau dans le vide.
            Vector3 normal = collision.GetContact(0).normal;
            Vector3 bounceDir = (normal * 1.5f - transform.forward * 0.5f + Vector3.up * 0.4f).normalized;
            rb.linearVelocity = bounceDir * 3.5f;
        }
        else
        {
            rb.linearVelocity = new Vector3(Random.Range(-1.5f, 1.5f), 1.5f, -2.5f);
        }

        rb.angularVelocity = new Vector3(Random.Range(-5f, 5f), Random.Range(-3f, 3f), Random.Range(-5f, 5f));
    }

    /// <summary>Surveille la chute et fige l'oiseau dès qu'il repose sur le vrai sol.</summary>
    private IEnumerator MonitorGroundLanding()
    {
        const float step = 0.05f;
        const float timeout = 4.8f;
        var wait = new WaitForSeconds(step);

        // Laisse au moins 0.35 s de culbute libre.
        float elapsed = 0.35f;
        yield return new WaitForSeconds(elapsed);

        while (!isGrounded && elapsed < timeout)
        {
            elapsed += step;

            // 1. Sol juste sous l'oiseau (mais pas l'obstacle aérien qui l'a tué, au début).
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 0.45f)
                && !hit.collider.isTrigger && !hit.collider.CompareTag("Player")
                && (hit.collider != killedByCollider || isGroundImpact || elapsed > 1.0f)
                && (SpeedSqr < 0.4f || elapsed > 0.8f))
            {
                break;
            }

            // 2. Plancher d'altitude atteint.
            if (transform.position.y <= currentMinHeight + 0.15f) break;

            // 3. L'oiseau s'est arrêté de rouler, et n'est pas suspendu au-dessus du vide.
            if (elapsed > 0.6f && SpeedSqr < 0.08f && Physics.Raycast(transform.position, Vector3.down, 1.2f)) break;

            yield return wait;
        }

        // Au pire, figé au bout du timeout, avant le rechargement de la scène.
        FreezeBirdOnGround();
    }

    /// <summary>Tant que le contact concerne l'obstacle aérien fatal, ne pas se figer dessus.</summary>
    private bool IsOnKillingObstacle(Collider other) =>
        other == killedByCollider && !isGroundImpact && Time.time - deathTime < 0.6f;

    /// <summary>Au moins un point de contact sur une surface orientée vers le haut (un sol).</summary>
    private static bool HasGroundContact(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.4f) return true;
        }
        return false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!enabled) return;

        if (!IsDead)
        {
            // Toute collision solide (rocher, montagne, obstacle) est mortelle.
            Debug.Log($"[MoveBird] Collision mortelle avec {collision.gameObject.name} !");
            Die(collision);
            return;
        }

        float sinceDeath = Time.time - deathTime;
        if (!isGrounded && sinceDeath >= 0.25f && !IsOnKillingObstacle(collision.collider)
            && HasGroundContact(collision) && (SpeedSqr < 0.4f || sinceDeath >= 0.8f))
        {
            FreezeBirdOnGround();
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!enabled || !IsDead || isGrounded || IsOnKillingObstacle(collision.collider)) return;

        float sinceDeath = Time.time - deathTime;
        if (sinceDeath >= 0.4f && HasGroundContact(collision) && (SpeedSqr < 0.3f || sinceDeath >= 1.0f))
        {
            FreezeBirdOnGround();
        }
    }

    /// <summary>Fige l'oiseau au sol : plus aucun tremblement ni glissement parasite.</summary>
    private void FreezeBirdOnGround()
    {
        if (isGrounded) return;
        isGrounded = true;
        FreezeRigidbody();
    }

    private void FreezeRigidbody()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;
    }

    /// <summary>La caméra détachée garde l'oiseau dans le cadre pendant sa chute.</summary>
    private IEnumerator FollowFallingBird(Transform camTransform)
    {
        const float duration = 6f;
        float elapsed = 0f;

        while (elapsed < duration && camTransform != null)
        {
            elapsed += Time.deltaTime;
            Vector3 dirToBird = transform.position - camTransform.position;
            if (dirToBird.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToBird, Vector3.up);
                camTransform.rotation = Quaternion.Slerp(camTransform.rotation, targetRot, Time.deltaTime * 3.5f);
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ éditeur

    /// <summary>Zone de vol courante (cyan) et plafond (jaune) dans la vue Scène.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!enableClamping) return;

        float minX = Application.isPlaying ? currentMinX : defaultMinX;
        float maxX = Application.isPlaying ? currentMaxX : defaultMaxX;
        float minH = Application.isPlaying ? currentMinHeight : defaultMinHeight;
        float maxH = Application.isPlaying ? currentMaxHeight : defaultMaxHeight;
        float z = transform.position.z;

        Gizmos.color = new Color(0f, 0.9f, 1f, 0.4f);
        Gizmos.DrawWireCube(
            new Vector3((minX + maxX) * 0.5f, (minH + maxH) * 0.5f, z),
            new Vector3(Mathf.Abs(maxX - minX), Mathf.Abs(maxH - minH), 12f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, maxH, z - 6f), new Vector3(maxX, maxH, z - 6f));
        Gizmos.DrawLine(new Vector3(maxX, maxH, z - 6f), new Vector3(maxX, maxH, z + 6f));
        Gizmos.DrawLine(new Vector3(maxX, maxH, z + 6f), new Vector3(minX, maxH, z + 6f));
        Gizmos.DrawLine(new Vector3(minX, maxH, z + 6f), new Vector3(minX, maxH, z - 6f));
    }
}
