using KiBird.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Met à jour l'écran de résultat sérialisé dans Blocks.unity.
/// La disposition et le style vivent dans la hiérarchie Unity, pas dans ce script.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [Header("Réinitialisation")]
    [Tooltip("Décoche pour garder l'écran de fin affiché indéfiniment (pratique pour tester/observer le visuel) : plus de redémarrage automatique, seul Espace/R/Entrée relance (si activé ci-dessous).")]
    public bool autoRestart = true;
    [Min(0.1f)] public float autoResetDelay = 5f;
    public bool allowInstantRestartKeys = true;

    [Header("Références - objet GameOverMenu")]
    public GameObject rootPanel;
    [Tooltip("Le HUD de jeu (GameMenu : score, temps de survie affichés en haut à gauche pendant le vol) à masquer quand ce panneau s'affiche.")]
    public GameObject gameMenuRoot;
    public Text statusText;
    public Text titleText;
    public Text scoreLabelText;
    public Text scoreText;
    public Text timeLabelText;
    public Text survivalTimeText;
    public Text bestScoreText;
    public Text countdownText;
    public Text restartHintText;
    public Image backgroundOverlay;
    public Image cardBg;
    public Image accentBar;
    public Image resultBadge;
    public Image statsPanelBg;
    public Image countdownPanelBg;
    public Image countdownProgress;

    [Header("Palette Défaite")]
    public Color defeatAccent = new Color(1f, 0.73f, 0.16f);
    public Color defeatCard = new Color(0.20f, 0.095f, 0.035f, 0.98f);
    public Color defeatOverlay = new Color(0.02f, 0.035f, 0.02f, 0.68f);
    public Color defeatSubPanel = new Color(0.10f, 0.055f, 0.025f, 0.86f);

    [Header("Palette Victoire")]
    public Color victoryAccent = new Color(0.54f, 0.90f, 0.32f);
    public Color victoryCard = new Color(0.12f, 0.22f, 0.07f, 0.98f);
    public Color victoryOverlay = new Color(0.015f, 0.08f, 0.025f, 0.65f);
    public Color victorySubPanel = new Color(0.055f, 0.14f, 0.045f, 0.86f);

    [Header("Nouveau Record")]
    public Color newRecordColor = new Color(1f, 0.85f, 0.15f);

    [Header("Sons")]
    public AudioSource gameOverSound;
    public AudioClip defeatSound;
    public AudioClip victorySound;

    private float countdownTimer;
    private bool isGameOverActive;

    private static readonly Color Cream = new Color(1f, 0.94f, 0.75f);
    private static readonly Color Moss = new Color(0.62f, 0.83f, 0.46f);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

#if UNITY_EDITOR
        if (victorySound == null)
        {
            victorySound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AssetStore/Sounds/victory.wav");
        }
#endif

        if (rootPanel == null)
        {
            Debug.LogError("[GameOverUI] L'objet GameOverMenu n'est pas assigné dans l'Inspector.");
            enabled = false;
            return;
        }

        rootPanel.SetActive(false);
    }

    private void OnEnable()
    {
        MoveBird.OnBirdDied += HandleBirdDied;
        MoveBird.OnBirdWon += HandleBirdWon;
    }

    private void OnDisable()
    {
        MoveBird.OnBirdDied -= HandleBirdDied;
        MoveBird.OnBirdWon -= HandleBirdWon;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandleBirdDied() => ShowGameOver(false);
    private void HandleBirdWon() => ShowGameOver(true);
    public void ShowVictory() => ShowGameOver(true);

    public void ShowGameOver(bool victory = false)
    {
        if (isGameOverActive || rootPanel == null) return;
        DisplayResult(victory, forceNewRecord: false);
    }

    // Clic droit sur le composant (en Play mode) > "Debug : Afficher Nouveau Record" pour
    // prévisualiser le panneau sans avoir à rejouer/battre le record à chaque fois.
    [ContextMenu("Debug : Afficher Nouveau Record")]
    private void DebugShowNewRecord()
    {
        if (rootPanel == null) return;
        isGameOverActive = false;
        DisplayResult(false, forceNewRecord: true);
    }

    [ContextMenu("Debug : Afficher Victoire")]
    private void DebugShowVictory()
    {
        if (rootPanel == null) return;
        isGameOverActive = false;
        DisplayResult(true, forceNewRecord: false);
    }

    [ContextMenu("Debug : Afficher Victoire + Nouveau Record")]
    private void DebugShowVictoryNewRecord()
    {
        if (rootPanel == null) return;
        isGameOverActive = false;
        DisplayResult(true, forceNewRecord: true);
    }

    private void DisplayResult(bool victory, bool forceNewRecord)
    {
        isGameOverActive = true;
        countdownTimer = Mathf.Max(0.1f, autoResetDelay);
        rootPanel.SetActive(true);
        if (gameMenuRoot != null) gameMenuRoot.SetActive(false);

        int currentScore = MoveBird.CurrentScore;
        int bestScore = ScoreManager.GetBestScore();
        bool isNewRecord = forceNewRecord || (currentScore > 0 && currentScore >= bestScore);

        ApplyTheme(victory, isNewRecord);
        if (scoreText != null) scoreText.text = $"{currentScore:N0} PTS";
        if (bestScoreText != null) bestScoreText.text = $"{bestScore:N0} PTS";

        int secondsTotal = Mathf.FloorToInt(MoveBird.SurvivalTime);
        if (survivalTimeText != null)
        {
            survivalTimeText.text = $"{secondsTotal / 60:D2}:{secondsTotal % 60:D2}";
        }

        UpdateCountdownLabel();
        PlayResultSound(victory);
    }

    private void Update()
    {
        if (!isGameOverActive) return;

        if (allowInstantRestartKeys && IsRestartKeyPressed())
        {
            RestartScene();
            return;
        }

        if (!autoRestart) return;

        countdownTimer -= Time.unscaledDeltaTime;
        UpdateCountdownLabel();

        if (countdownTimer <= 0f) RestartScene();
    }

    private void ApplyTheme(bool victory, bool isNewRecord)
    {
        Color accent = victory ? victoryAccent : defeatAccent;
        Color subPanel = victory ? victorySubPanel : defeatSubPanel;

        // Uniquement l'annonce de nouveau record : pas de texte "FIN DU VOL" / "PARCOURS
        // TERMINÉ" générique (retiré de la scène), donc on masque statusText le reste du temps
        // plutôt que de lui laisser un texte par défaut.
        if (statusText != null)
        {
            statusText.gameObject.SetActive(isNewRecord);
            if (isNewRecord)
            {
                statusText.text = "NOUVEAU RECORD !";
                statusText.color = newRecordColor;

                if (statusText.GetComponent<PulseEffect>() == null)
                {
                    statusText.gameObject.AddComponent<PulseEffect>();
                }

                SpawnRecordConfetti();
            }
        }
        if (titleText != null)
        {
            titleText.text = victory ? "VICTOIRE !" : "GAME OVER";
            titleText.color = accent;
        }
        if (scoreLabelText != null) scoreLabelText.text = victory ? "SCORE FINAL" : "SCORE DU VOL";
        if (timeLabelText != null) timeLabelText.text = victory ? "TEMPS DE VOL" : "TEMPS DE SURVIE";
        if (restartHintText != null)
        {
            restartHintText.text = allowInstantRestartKeys
                ? "ESPACE POUR REJOUER"
                : "PRÉPAREZ LE PROCHAIN PILOTE";
        }

        if (backgroundOverlay != null) backgroundOverlay.color = victory ? victoryOverlay : defeatOverlay;
        if (cardBg != null) cardBg.color = victory ? victoryCard : defeatCard;
        if (accentBar != null) accentBar.color = accent;
        if (resultBadge != null) resultBadge.color = accent;
        if (statsPanelBg != null) statsPanelBg.color = subPanel;
        if (countdownPanelBg != null) countdownPanelBg.color = subPanel;
        if (countdownProgress != null) countdownProgress.color = accent;
        if (scoreText != null) scoreText.color = Cream;
    }

    // Confettis UI (pas le ParticleSystem 3D de ConfettiEffect/HoopScore : celui-ci serait
    // rendu par la caméra donc toujours DERRIÈRE un Canvas Screen Space - Overlay). Centrés
    // sur le texte "NOUVEAU RECORD !" et garantis au premier plan.
    private void SpawnRecordConfetti()
    {
        UIConfettiBurst.SpawnBurst(statusText.rectTransform);
    }

    private void UpdateCountdownLabel()
    {
        if (!autoRestart)
        {
            if (countdownText != null)
            {
                countdownText.text = allowInstantRestartKeys ? "ESPACE POUR CONTINUER" : "";
            }
            if (countdownProgress != null) countdownProgress.fillAmount = 0f;
            return;
        }

        int remaining = Mathf.Max(0, Mathf.CeilToInt(countdownTimer));
        if (countdownText != null) countdownText.text = $"NOUVEAU VOL DANS {remaining}s";
        if (countdownProgress != null)
        {
            countdownProgress.fillAmount = Mathf.Clamp01(countdownTimer / Mathf.Max(0.1f, autoResetDelay));
        }
    }

    private void PlayResultSound(bool victory)
    {
        AudioClip clip = victory ? victorySound : defeatSound;
        if (clip == null && !victory && gameOverSound != null) clip = gameOverSound.clip;
        if (clip == null) return;

        if (gameOverSound != null)
        {
            gameOverSound.Stop();
            gameOverSound.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
    }

    private bool IsRestartKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame ||
                                    keyboard.enterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R) ||
               Input.GetKeyDown(KeyCode.Return);
#endif
    }

    public void RestartScene()
    {
        isGameOverActive = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
