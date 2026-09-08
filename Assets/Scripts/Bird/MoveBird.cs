using UnityEngine;
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

    private static readonly int FlyingHash = Animator.StringToHash("Flying");

    private Quaternion initialRotation;
    private float currentRoll = 0f;
    private float currentPitch = 0f;
    private float keyboardFlapTimer = 0f;

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
        UpdateAnimationSpeed();
    }

    private void Update()
    {
        HandleSpeedInput();
        Vector3 input = GetInput();
        Move(input);
        Bank(input);
        UpdateAnimation(input);
    }

    private void OnValidate()
    {
        UpdateAnimationSpeed();
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
        bool isTurning = Mathf.Abs(input.x) > turnAnimationThreshold;
        bool isClimbing = input.y > climbAnimationThreshold;
        bool isFlying = isTurning || isClimbing;

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
}