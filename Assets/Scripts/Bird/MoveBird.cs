using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MoveBird : MonoBehaviour
{
    [Header("Movement")]
    public Vector3 moveMatrix = new Vector3(0, 0, 1);
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

    [Header("Boundary Clamping (Montagnes & Altitude)")]
    [Tooltip("Active le confinement de l'oiseau dans les limites latérales des montagnes et d'altitude.")]
    public bool enableClamping = true;

    [Tooltip("Limite X gauche (montagne gauche).")]
    public float defaultMinX = -0.97f;

    [Tooltip("Limite X droite (montagne droite).")]
    public float defaultMaxX = 8.84f;

    [Tooltip("Altitude maximale. Au plafond, l'oiseau replane automatiquement.")]
    public float defaultMaxHeight = 9.5f;

    [Tooltip("Altitude minimale (sol/eau).")]
    public float defaultMinHeight = 1.5f;

    [Tooltip("Si décoché (recommandé), utilise directement les limites ci-dessus. Si coché, permet aux composants BlockBounds de redéfinir les limites par bloc.")]
    public bool useBlockBounds = false;

    [Tooltip("Durée de planage forcé après avoir touché le plafond (l'oiseau ne peut plus remonter immédiatement).")]
    public float ceilingRecoveryDuration = 0.6f;

    [Tooltip("Vitesse d'interpolation des limites lors du passage d'un bloc à l'autre (si useBlockBounds est activé).")]
    public float boundsTransitionSpeed = 8f;

    [Header("Score & Survival (Game Over)")]
    [Tooltip("Points gagnés par mètre parcouru le long de l'axe Z.")]
    public float scorePerMeter = 1f;

    /// <summary>Score statique actuel du joueur (accessible partout).</summary>
    public static int CurrentScore = 0;

    /// <summary>Temps de survie actuel en secondes.</summary>
    public static float SurvivalTime = 0f;

    /// <summary>L'oiseau est-il mort ?</summary>
    public static bool IsDead = false;

    /// <summary>L'oiseau a-t-il atteint le nid et remporté la partie ?</summary>
    public static bool IsWon = false;

    /// <summary>Événement déclenché à la mort de l'oiseau.</summary>
    public static event System.Action OnBirdDied;

    /// <summary>Événement déclenché lors de la victoire.</summary>
    public static event System.Action OnBirdWon;

    private static int bonusScore = 0;
    private float startZPos = 0f;
    
    [Header("UI Menu & Sound Effect")]
    [SerializeField] private Text scoreText;
    private AudioSource birdSource;
    [SerializeField] private AudioClip dieSound;
    [SerializeField] private AudioClip mainMusic;

    public static void AddBonusScore(int points)
    {
        bonusScore += points;
        CurrentScore += points;
        KiBird.MainMenu.ScoreManager.UpdateSessionBest(CurrentScore);
    }

    /// <summary>Table des scores 0..1999 sous forme de string, construite une seule fois.</summary>
    private static readonly string[] ScoreStringCache = BuildScoreStringCache();

    private static string[] BuildScoreStringCache()
    {
        var cache = new string[2000];
        for (int i = 0; i < cache.Length; i++)
        {
            cache[i] = i.ToString();
        }
        return cache;
    }

    private static readonly int FlyingHash = Animator.StringToHash("Flying");

    private Quaternion initialRotation;
    private float currentRoll = 0f;
    private float currentPitch = 0f;
    private float keyboardFlapTimer = 0f;
    private float ceilingLockoutTimer = 0f;

    private float currentMinX;
    private float currentMaxX;
    private float currentMaxHeight;
    private float currentMinHeight;
    private BlockBounds currentBlockBounds;
    private Rigidbody rb;
    private bool isGrounded = false;
    private float deathTime = 0f;
    private Collider killedByCollider = null;
    private bool isGroundImpact = false;
    private int displayedScore = -1;

    [Header("Effets Visuels & Mort")]
    [Tooltip("Préfab ou référence vers le système de particules de plumes (optionnel, auto-généré si vide).")]
    [SerializeField] private ParticleSystem featherParticlePrefab;

    /// <summary>
    /// Écouteur à interroger : celui câblé dans l'inspecteur s'il y en a un, sinon celui créé
    /// automatiquement par KinectInputSource. Résolu à chaque accès plutôt que mis en cache dans
    /// Awake() : le bootstrap tourne après les Awake de la scène, il serait encore null.
    /// </summary>
    private KinectInputSource ActiveKinectSource =>
        kinectSource != null ? kinectSource : KinectInputSource.Instance;

    /// <summary>La Kinect pilote l'oiseau en ce moment (utilisé aussi pour l'UI/debug).</summary>
    public bool IsKinectDriving
    {
        get
        {
            if (!useKinectWhenAvailable) return false;
            var source = ActiveKinectSource;
            return source != null && source.HasPlayer;
        }
    }

    private void Awake()
    {
        CurrentScore = 0;
        SurvivalTime = 0f;
        IsDead = false;
        IsWon = false;
        isGrounded = false;
        deathTime = 0f;
        killedByCollider = null;
        isGroundImpact = false;
        bonusScore = 0;
        startZPos = transform.position.z;
        birdSource = GetComponent<AudioSource>();

        // Configuration du Rigidbody dynamique pour détecter les collisions avec le décor (MeshColliders statiques)
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
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

        if (gameObject.CompareTag("Untagged"))
        {
            gameObject.tag = "Player";
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }
        if (animator != null)
        {
            animator.SetBool(FlyingHash, false);
        }
        initialRotation = transform.localRotation;
        currentMinX = defaultMinX;
        currentMaxX = defaultMaxX;
        currentMaxHeight = defaultMaxHeight;
        currentMinHeight = defaultMinHeight;
        UpdateAnimationSpeed();

        displayedScore = -1;
        RefreshScoreLabel();


    }

    private void Update()
    {
        if (IsDead || IsWon) return;

        SurvivalTime += Time.deltaTime;
        float dist = Mathf.Max(0f, transform.position.z - startZPos);
        int newScore = Mathf.FloorToInt(dist * scorePerMeter) + bonusScore;
        if (newScore != CurrentScore)
        {
            CurrentScore = newScore;
            KiBird.MainMenu.ScoreManager.UpdateSessionBest(CurrentScore);
        }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            Die();
            return;
        }
#else
        if (Input.GetKeyDown(KeyCode.K))
        {
            Die();
            return;
        }
#endif

        HandleSpeedInput();
        UpdateBoundaries();
        Vector3 input = GetInput();
        input = EnforceBoundaryConstraints(input);
        Move(input);
        Bank(input);
        UpdateAnimation(input);

        RefreshScoreLabel();
    }

    /// <summary>
    /// Neutralise la vélocité résiduelle du Rigidbody au rythme de la physique (50 Hz) et non à
    /// chaque frame de rendu : tant que l'oiseau est vivant c'est le Transform qui le pilote,
    /// PhysX ne doit rien ajouter par-dessus. Inutile après la mort ou la victoire, où le
    /// Rigidbody reprend la main (chute libre) ou est figé.
    /// </summary>
    private void FixedUpdate()
    {
        if (rb == null || IsDead || IsWon) return;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Réécrit le label de score uniquement quand la valeur entière change. Écrire dans un
    /// UnityEngine.UI.Text à chaque frame alloue une string (GC) et marque le Canvas dirty, ce qui
    /// force un rebuild uGUI complet à chaque image.
    /// </summary>
    private void RefreshScoreLabel()
    {
        if (scoreText == null || displayedScore == CurrentScore) return;

        displayedScore = CurrentScore;
        scoreText.text = GetScoreString(CurrentScore);
    }

    /// <summary>Strings de score pré-calculées pour éviter une allocation par changement de score.</summary>
    private static string GetScoreString(int score)
    {
        if (score < 0 || score >= ScoreStringCache.Length) return score.ToString();

        return ScoreStringCache[score];
    }

    /// <summary>
    /// Déclenche la victoire du joueur lorsqu'il atteint le nid (Ending Block).
    /// </summary>
    public void Win(int finishBonus = 500)
    {
        if (IsDead || IsWon) return;

        IsWon = true;

        if (birdSource != null)
        {
            birdSource.Stop(); // Arrêter la musique de vol
        }

        forwardSpeed = 0f;
        horizontalSpeed = 0f;
        verticalSpeed = 0f;

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
#endif
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        if (animator != null)
        {
            animator.SetBool(FlyingHash, false);
        }

        if (finishBonus > 0)
        {
            AddBonusScore(finishBonus);
        }

        KiBird.MainMenu.ScoreManager.AddScore(CurrentScore);
        Debug.Log($"[MoveBird] Victoire ! L'oiseau s'est posé dans le nid. Score final : {CurrentScore} pts | Survie : {SurvivalTime:F1}s");
        OnBirdWon?.Invoke();

        if (GameOverUI.Instance == null)
        {
            var go = new GameObject("GameOverManager");
            go.AddComponent<GameOverUI>();
        }

        GameOverUI.Instance.ShowVictory();
    }

    /// <summary>
    /// Déclenche la mort de l'oiseau, active la gravité physique pour le faire chuter,
    /// émet un éclat de plumes, enregistre le score et prévient le Game Over.
    /// </summary>
    public void Die(Collision collision = null)
    {
        if (IsDead) return;

        IsDead = true;

        if (birdSource != null && dieSound != null)
        {
            birdSource.Stop(); // stopping main music if playing
            birdSource.PlayOneShot(dieSound);
        }

        deathTime = Time.time;
        forwardSpeed = 0f;
        horizontalSpeed = 0f;
        verticalSpeed = 0f;

        // Émission des particules de plumes à l'impact mortel
        SpawnFeatherExplosion();

        // Désactive l'animateur pour laisser la physique ragdoll/chute libre s'exprimer
        if (animator != null)
        {
            animator.SetBool(FlyingHash, false);
            animator.enabled = false;
        }

        killedByCollider = collision != null ? collision.collider : null;

        // Détecte si l'impact initial a eu lieu directement avec le sol/l'eau ou en altitude
        isGroundImpact = (collision == null)
            || (collision.collider is TerrainCollider)
            || collision.gameObject.name.ToLower().Contains("terrain")
            || collision.gameObject.name.ToLower().Contains("water")
            || (transform.position.y <= currentMinHeight + 0.8f);

        // Active la gravité et libère les rotations pour que l'oiseau culbute et tombe sous l'effet de la gravité
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.None;
            // À partir d'ici c'est la physique qui pilote le Transform : l'interpolation redevient
            // utile (chute lissée entre les pas à 50 Hz) au lieu d'entrer en conflit avec le script.
            rb.interpolation = RigidbodyInterpolation.Interpolate;
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = 0.5f;
            rb.angularDamping = 1.0f;
#else
            rb.drag = 0.5f;
            rb.angularDrag = 1.0f;
#endif

            // Matériau physique avec friction élevée pour qu'il s'arrête naturellement au sol sans glisser
            Collider birdCol = GetComponent<Collider>();
            if (birdCol != null)
            {
                PhysicsMaterial tumbleMat = new PhysicsMaterial("DeadBirdTumble")
                {
                    dynamicFriction = 0.7f,
                    staticFriction = 0.9f,
                    bounciness = 0.15f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
                birdCol.material = tumbleMat;
            }

            Vector3 tumbleImpulse;
            if (collision != null && collision.contactCount > 0)
            {
                // Rebond énergique dans la direction opposée à la surface touchée pour l'éjecter dans le vide
                Vector3 normal = collision.contacts[0].normal;
                Vector3 bounceDir = (normal * 1.5f - transform.forward * 0.5f + Vector3.up * 0.4f).normalized;
                tumbleImpulse = bounceDir * 3.5f;
            }
            else
            {
                tumbleImpulse = new Vector3(
                    Random.Range(-1.5f, 1.5f),
                    1.5f,
                    -2.5f
                );
            }

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = tumbleImpulse;
            rb.angularVelocity = new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(-3f, 3f),
                Random.Range(-5f, 5f)
            );
#else
            rb.velocity = tumbleImpulse;
            rb.angularVelocity = new Vector3(
                Random.Range(-5f, 5f),
                Random.Range(-3f, 3f),
                Random.Range(-5f, 5f)
            );
#endif
        }

        // Surveillance de la chute pour figer l'oiseau UNIQUEMENT dès qu'il touche le vrai sol
        StartCoroutine(MonitorGroundLanding());

        // Sauvegarde immédiate dans le classement persistant
        KiBird.MainMenu.ScoreManager.AddScore(CurrentScore);

        Debug.Log($"[MoveBird] L'oiseau est mort ! Score final : {CurrentScore} pts | Survie : {SurvivalTime:F1}s");
        OnBirdDied?.Invoke();

        // Détacher la caméra pour qu'elle suive la chute de manière stable sans vriller avec la carcasse
        Camera mainCam = GetComponentInChildren<Camera>();
        if (mainCam != null)
        {
            mainCam.transform.SetParent(null, true);
            StartCoroutine(FollowFallingBird(mainCam.transform));
        }

        // Si aucun GameOverUI n'est dans la scène, on le crée automatiquement
        if (GameOverUI.Instance == null)
        {
            var go = new GameObject("GameOverManager");
            go.AddComponent<GameOverUI>();
        }
    }

    /// <summary>
    /// Fige complètement l'oiseau une fois au sol pour couper net tout mouvement
    /// et empêcher tout tremblement ou glissement parasite.
    /// </summary>
    private void FreezeBirdOnGround()
    {
        if (isGrounded) return;
        isGrounded = true;

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
#endif
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        Debug.Log("[MoveBird] Oiseau stabilisé au sol : physique et mouvements totalement figés.");
    }

    private System.Collections.IEnumerator MonitorGroundLanding()
    {
        // Laisse au moins 0.35s de culbute libre sans bloquer
        yield return new WaitForSeconds(0.35f);

        float timeout = 4.8f;
        float elapsed = 0.35f;

        while (!isGrounded && IsDead && elapsed < timeout)
        {
            elapsed += 0.05f;

#if UNITY_6000_0_OR_NEWER
            float currentSpeedSqr = rb != null ? rb.linearVelocity.sqrMagnitude : 0f;
#else
            float currentSpeedSqr = rb != null ? rb.velocity.sqrMagnitude : 0f;
#endif

            // 1. Raycast sous l'oiseau vers le bas pour détecter le sol
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 0.45f))
            {
                if (!hit.collider.isTrigger && !hit.collider.CompareTag("Player"))
                {
                    // Ne pas figer sur l'obstacle aérien d'origine dans les premières fractions de seconde
                    if (hit.collider != killedByCollider || isGroundImpact || elapsed > 1.0f)
                    {
                        if (currentSpeedSqr < 0.4f || elapsed > 0.8f)
                        {
                            FreezeBirdOnGround();
                            yield break;
                        }
                    }
                }
            }

            // 2. Plancher d'altitude absolu atteint
            if (transform.position.y <= currentMinHeight + 0.15f)
            {
                FreezeBirdOnGround();
                yield break;
            }

            // 3. Stabilisation de la vitesse (l'oiseau s'est arrêté après avoir roulé au sol)
            if (elapsed > 0.6f && currentSpeedSqr < 0.08f)
            {
                // Vérifier qu'on n'est pas suspendu en l'air au-dessus du vide
                if (Physics.Raycast(transform.position, Vector3.down, 1.2f))
                {
                    FreezeBirdOnGround();
                    yield break;
                }
            }

            yield return new WaitForSeconds(0.05f);
        }

        // Sécurité finale : figer complètement avant le reload de la scène
        if (!isGrounded && IsDead)
        {
            FreezeBirdOnGround();
        }
    }

    private void SpawnFeatherExplosion()
    {
        if (featherParticlePrefab != null)
        {
            ParticleSystem psInstance = Instantiate(featherParticlePrefab, transform.position, Quaternion.identity);
            psInstance.Play();
            Destroy(psInstance.gameObject, 4f);
            return;
        }

        // Génération dynamique du burst de plumes
        GameObject fxObj = new GameObject("FeatherExplosion");
        fxObj.transform.position = transform.position;
        fxObj.transform.rotation = Quaternion.identity;

        ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
        ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();

        Material featherMat = null;
#if UNITY_EDITOR
        featherMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Generated/M_Feather.mat");
#endif
        if (featherMat == null)
        {
            Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (pShader != null) featherMat = new Material(pShader);
        }
        if (featherMat != null)
        {
            psr.material = featherMat;
        }

        var main = ps.main;
        main.duration = 1.0f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.gravityModifier = 0.12f; // Flottement aérien naturel de plumes
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0.0f, (short)35, (short)55)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0.5f),
                new GradientColorKey(new Color(0.75f, 0.85f, 0.95f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = grad;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-150f * Mathf.Deg2Rad, 150f * Mathf.Deg2Rad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0.0f, 0.6f);
        sizeCurve.AddKey(0.15f, 1.0f);
        sizeCurve.AddKey(0.75f, 1.0f);
        sizeCurve.AddKey(1.0f, 0.0f);
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

        ps.Play();
        Destroy(fxObj, 4.0f);
    }

    private System.Collections.IEnumerator FollowFallingBird(Transform camTransform)
    {
        float duration = 6f;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (!enabled) return;

        if (!IsDead)
        {
            // Toute collision solide avec un rocher, montagne ou obstacle tue l'oiseau
            Debug.Log($"[MoveBird] Collision mortelle avec {collision.gameObject.name} !");
            Die(collision);
        }
        else if (!isGrounded && Time.time - deathTime >= 0.25f)
        {
            // Si on touche encore l'obstacle aérien d'origine, ne pas se figer dessus
            if (collision.collider == killedByCollider && !isGroundImpact && Time.time - deathTime < 0.6f)
            {
                return;
            }

            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y > 0.4f)
                {
#if UNITY_6000_0_OR_NEWER
                    float speedSqr = rb != null ? rb.linearVelocity.sqrMagnitude : 0f;
#else
                    float speedSqr = rb != null ? rb.velocity.sqrMagnitude : 0f;
#endif
                    if (speedSqr < 0.4f || Time.time - deathTime >= 0.8f)
                    {
                        FreezeBirdOnGround();
                        break;
                    }
                }
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!enabled || !IsDead || isGrounded) return;

        // Ne pas se figer contre l'obstacle en l'air qui a provoqué la mort
        if (collision.collider == killedByCollider && !isGroundImpact && Time.time - deathTime < 0.6f)
        {
            return;
        }

        if (Time.time - deathTime >= 0.4f)
        {
#if UNITY_6000_0_OR_NEWER
            float speedSqr = rb != null ? rb.linearVelocity.sqrMagnitude : 0f;
#else
            float speedSqr = rb != null ? rb.velocity.sqrMagnitude : 0f;
#endif
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y > 0.4f && (speedSqr < 0.3f || Time.time - deathTime >= 1.0f))
                {
                    FreezeBirdOnGround();
                    break;
                }
            }
        }
    }

    //private void OnTriggerEnter(Collider other)
    //{
    //    if (!enabled || IsDead) return;

    //    // Les anneaux (HoopScore) accordent des bonus et ne tuent pas
    //    if (other.GetComponent<HoopScore>() != null || other.GetComponentInParent<HoopScore>() != null)
    //    {
    //        return;
    //    }

    //    // Collision avec obstacles configurés en Trigger (eau, killzones, etc.)
    //    if (other.CompareTag("Obstacle") || other.CompareTag("Death") ||
    //        other.name.ToLower().Contains("obstacle") || other.name.ToLower().Contains("water"))
    //    {
    //        Debug.Log($"[MoveBird] Trigger mortel avec {other.gameObject.name} !");
    //        Die();
    //    }
    //}

    private void UpdateBoundaries()
    {
        if (!enableClamping) return;

        // Si l'écrasement par bloc est désactivé (recommandé), on utilise directement les valeurs configurées sur MoveBird
        if (!useBlockBounds)
        {
            currentMinX = defaultMinX;
            currentMaxX = defaultMaxX;
            currentMaxHeight = defaultMaxHeight;
            currentMinHeight = defaultMinHeight;
            return;
        }

        float birdZ = transform.position.z;
        currentBlockBounds = BlockBounds.GetBoundsAtZ(birdZ);

        float targetMinX = currentBlockBounds != null ? currentBlockBounds.minX : defaultMinX;
        float targetMaxX = currentBlockBounds != null ? currentBlockBounds.maxX : defaultMaxX;
        float targetMaxH = currentBlockBounds != null ? currentBlockBounds.maxHeight : defaultMaxHeight;
        float targetMinH = currentBlockBounds != null ? currentBlockBounds.minHeight : defaultMinHeight;

        currentMinX = Mathf.Lerp(currentMinX, targetMinX, Time.deltaTime * boundsTransitionSpeed);
        currentMaxX = Mathf.Lerp(currentMaxX, targetMaxX, Time.deltaTime * boundsTransitionSpeed);
        currentMaxHeight = Mathf.Lerp(currentMaxHeight, targetMaxH, Time.deltaTime * boundsTransitionSpeed);
        currentMinHeight = Mathf.Lerp(currentMinHeight, targetMinH, Time.deltaTime * boundsTransitionSpeed);
    }

    private Vector3 EnforceBoundaryConstraints(Vector3 input)
    {
        if (!enableClamping) return input;

        // Plafond d'altitude : quand l'oiseau arrive à la hauteur maximale, déclenche le planage forcé
        if (transform.position.y >= currentMaxHeight - 0.05f)
        {
            ceilingLockoutTimer = ceilingRecoveryDuration;
            keyboardFlapTimer = 0f;
        }

        if (ceilingLockoutTimer > 0f)
        {
            ceilingLockoutTimer -= Time.deltaTime;
            // Force le planage vers le bas (au minimum glideSink, ou diveSink si l'utilisateur appuie pour piquer)
            if (input.y > glideSink)
            {
                input.y = glideSink;
            }
            keyboardFlapTimer = 0f;
        }

        // Plancher d'altitude : empêche de piquer sous le sol ou l'eau
        if (transform.position.y <= currentMinHeight + 0.05f)
        {
            if (input.y < 0f)
            {
                input.y = 0f;
                Die(); // L'oiseau s'écrase au sol ou dans l'eau
            }
        }

        // Confinement horizontal : empêche de braquer davantage dans la paroi rocheuse
        if (transform.position.x <= currentMinX + 0.05f && input.x < 0f)
        {
            input.x = 0f;
        }
        else if (transform.position.x >= currentMaxX - 0.05f && input.x > 0f)
        {
            input.x = 0f;
        }

        return input;
    }

    private void OnValidate()
    {
        UpdateAnimationSpeed();
        if (!useBlockBounds)
        {
            currentMinX = defaultMinX;
            currentMaxX = defaultMaxX;
            currentMaxHeight = defaultMaxHeight;
            currentMinHeight = defaultMinHeight;
        }
    }

    private Vector3 GetInput()
    {
        // Kinect prioritaire tant qu'un joueur est verrouillé et que les paquets sont frais ;
        // dès que ce n'est plus le cas, on retombe sans transition sur le clavier ci-dessous
        // (indispensable pour les tests et pour les animateurs pendant la JPO).
        if (IsKinectDriving) return ActiveKinectSource.Input;

        float x = 0f;
        float y = glideSink; // Vol plané naturel par défaut (identique à la Kinect bras à l'horizontale)
        float z = 0f;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            // Virage : Q / A / Flèche Gauche (gauche) ou D / Flèche Droite (droite)
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed || keyboard.qKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;

            // Battement / Montée : Espace ou Flèche Haut
            bool flapHeld = keyboard.spaceKey.isPressed || keyboard.upArrowKey.isPressed;
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            {
                keyboardFlapTimer = keyboardFlapDuration;
            }

            // Chute / Piqué : Ctrl, C ou Flèche Bas
            bool divePressed = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed || keyboard.downArrowKey.isPressed;

            if (flapHeld)
            {
                keyboardFlapTimer = keyboardFlapDuration;
                y = 1f;
            }
            else if (keyboardFlapTimer > 0f)
            {
                keyboardFlapTimer -= Time.deltaTime;
                // Décroissance douce de l'impulsion (comme gestures.py lift_impulse)
                y = Mathf.Clamp01(keyboardFlapTimer / keyboardFlapDuration);
            }
            else if (divePressed)
            {
                y = diveSink;
            }
            else
            {
                y = glideSink;
            }

            // Vitesse : Z / W (accélérer) ou S (ralentir)
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
#else
        x = Input.GetAxis("Horizontal");
        z = Input.GetAxis("Vertical");
        if (Input.GetKey(KeyCode.Space)) y = 1f;
        else if (Input.GetKey(KeyCode.LeftControl)) y = diveSink;
        else y = glideSink;
#endif

        return new Vector3(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
    }

    private bool IsBoosting()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)) return true;
        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.rightTrigger.ReadValue() > 0.2f) return true;
        return false;
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    private void HandleSpeedInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.numpadPlusKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame) forwardSpeed += 1f;
        if (keyboard.numpadMinusKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame) forwardSpeed = Mathf.Max(1f, forwardSpeed - 1f);

        if (keyboard.pageUpKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame) SetAnimationSpeed(animationSpeed + 0.25f);
        if (keyboard.pageDownKey.wasPressedThisFrame || keyboard.oKey.wasPressedThisFrame) SetAnimationSpeed(animationSpeed - 0.25f);
#endif
    }

    private void Move(Vector3 input)
    {
        float speedMod = IsBoosting() ? boostMultiplier : 1f;
        float fwd = autoMoveForward ? (forwardSpeed + (input.z * forwardSpeed * 0.4f)) : (input.z * forwardSpeed);
        fwd *= speedMod;

        Vector3 move = new Vector3(
            input.x * horizontalSpeed * speedMod * Time.deltaTime,
            input.y * verticalSpeed * speedMod * Time.deltaTime,
            fwd * Time.deltaTime
        );

        transform.Translate(move, Space.World);

        if (enableClamping)
        {
            Vector3 pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, currentMinX, currentMaxX);
            pos.y = Mathf.Clamp(pos.y, currentMinHeight, currentMaxHeight);
            transform.position = pos;
        }
    }

    private void Bank(Vector3 input)
    {
        if (!enableBanking) return;

        float targetRoll = -input.x * bankAngle;
        float targetPitch = -input.y * pitchAngle;

        currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * rotationSpeed);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * rotationSpeed);

        transform.localRotation = initialRotation * Quaternion.Euler(currentPitch, 0f, currentRoll);
    }

    private void UpdateAnimation(Vector3 input)
    {
        if (animator == null) return;

        // Logique demandée :
        // - En mode planage ou en chute -> Flying = false (joue l'animation idle/planage)
        // - Si l'oiseau tourne OU remonte vers le haut -> Flying = true (joue l'animation Flying/battement)
        // - Si le plafond d'altitude est atteint (planage forcé) -> Flying = false garanti
        bool isTurning = Mathf.Abs(input.x) > turnAnimationThreshold;
        bool isClimbing = input.y > climbAnimationThreshold;
        bool isFlying = (isTurning || isClimbing) && ceilingLockoutTimer <= 0f;

        animator.SetBool(FlyingHash, isFlying);

        if (syncAnimationWithSpeed && forwardSpeed > 0.01f)
        {
            float speedMod = IsBoosting() ? boostMultiplier : 1f;
            animator.speed = animationSpeed * speedMod;
        }
        else
        {
            animator.speed = animationSpeed;
        }
    }

    public void SetForwardSpeed(float newSpeed)
    {
        forwardSpeed = Mathf.Max(0f, newSpeed);
    }

    public void SetAnimationSpeed(float newSpeed)
    {
        animationSpeed = Mathf.Clamp(newSpeed, 0.1f, 10f);
        UpdateAnimationSpeed();
    }

    private void UpdateAnimationSpeed()
    {
        if (animator != null)
        {
            animator.speed = animationSpeed;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableClamping) return;

        float minX = Application.isPlaying ? currentMinX : defaultMinX;
        float maxX = Application.isPlaying ? currentMaxX : defaultMaxX;
        float minH = Application.isPlaying ? currentMinHeight : defaultMinHeight;
        float maxH = Application.isPlaying ? currentMaxHeight : defaultMaxHeight;

        Gizmos.color = new Color(0f, 0.9f, 1f, 0.4f);
        float z = transform.position.z;
        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minH + maxH) * 0.5f, z);
        Vector3 size = new Vector3(Mathf.Abs(maxX - minX), Mathf.Abs(maxH - minH), 12f);
        Gizmos.DrawWireCube(center, size);

        // Ligne jaune au plafond
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, maxH, z - 6f), new Vector3(maxX, maxH, z - 6f));
        Gizmos.DrawLine(new Vector3(maxX, maxH, z - 6f), new Vector3(maxX, maxH, z + 6f));
        Gizmos.DrawLine(new Vector3(maxX, maxH, z + 6f), new Vector3(minX, maxH, z + 6f));
        Gizmos.DrawLine(new Vector3(minX, maxH, z + 6f), new Vector3(minX, maxH, z - 6f));
    }

    public void PlayMainMusic()
    {
        if (birdSource != null && mainMusic != null)
        {
            birdSource.clip = mainMusic;
            birdSource.loop = true;
            birdSource.Play();
        }
    }
}