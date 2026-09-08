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

    [Header("Animation")]
    public Animator animator;
    [Range(0.1f, 10f)] public float animationSpeed = 2.5f;
    public bool syncAnimationWithSpeed = true;

    private Quaternion initialRotation;
    private float currentRoll = 0f;
    private float currentPitch = 0f;

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
        initialRotation = transform.localRotation;
        UpdateAnimationSpeed();
    }

    private void Update()
    {
        HandleSpeedInput();
        Vector3 input = GetInput();
        Move(input);
        Bank(input);
        UpdateAnimation();
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
        float y = 0f;
        float z = 0f;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed || keyboard.qKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;

            if (keyboard.spaceKey.isPressed) y += 1f;
            if (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed) y -= 1f;

            if (keyboard.wKey.isPressed || keyboard.zKey.isPressed || keyboard.upArrowKey.isPressed) z += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) z -= 1f;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            x += stick.x;
            y += stick.y;
        }
#else
        x = Input.GetAxis("Horizontal");
        z = Input.GetAxis("Vertical");
        if (Input.GetKey(KeyCode.Space)) y += 1f;
        if (Input.GetKey(KeyCode.LeftControl)) y -= 1f;
#endif

        return new Vector3(x, y, z);
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

    private void UpdateAnimation()
    {
        if (animator == null) return;

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