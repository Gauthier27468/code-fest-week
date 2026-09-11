using KiBird.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Écran de fin de partie (défaite ou victoire) : score, temps, record, puis rechargement
/// automatique de la scène pour le joueur suivant.
/// La disposition et le style vivent dans la hiérarchie Unity (Blocks.unity), pas dans ce script.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [Header("Réinitialisation")]
    [Tooltip("Décocher pour garder l'écran de fin affiché indéfiniment (test du visuel) : seul Espace/R/Entrée relance alors, si autorisé ci-dessous.")]
    public bool autoRestart = true;
    [Min(0.1f)] public float autoResetDelay = 5f;
    public bool allowInstantRestartKeys = true;

    [Header("Références - objet GameOverMenu")]
    public GameObject rootPanel;
    [Tooltip("HUD de jeu (score, temps) à masquer quand ce panneau s'affiche.")]
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

    private static readonly Color Cream = new Color(1f, 0.94f, 0.75f);

    private float countdownTimer;
    private bool isGameOverActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

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

    private void HandleBirdDied() => ShowResult(victory: false);
    private void HandleBirdWon() => ShowResult(victory: true);

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

    public void RestartScene()
    {
        isGameOverActive = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ------------------------------------------------------------------ affichage

    private void ShowResult(bool victory, bool forceNewRecord = false)
    {
        if (isGameOverActive) return;

        isGameOverActive = true;
        countdownTimer = autoResetDelay;
        rootPanel.SetActive(true);
        if (gameMenuRoot != null) gameMenuRoot.SetActive(false);

        // bestScore vient du classement d'AVANT ce vol : MoveBird n'appelle AddScore qu'après
        // l'événement, pour que la comparaison se fasse avec l'ancien record.
        int currentScore = MoveBird.CurrentScore;
        int bestScore = ScoreManager.GetBestScore();
        bool isNewRecord = forceNewRecord || (currentScore > 0 && currentScore > bestScore);

        ApplyTheme(victory, isNewRecord);

        if (scoreText != null) scoreText.text = $"{currentScore:N0} PTS";
        // Record battu : affiche le nouveau record plutôt que celui qu'on vient de dépasser.
        if (bestScoreText != null) bestScoreText.text = $"{(isNewRecord ? currentScore : bestScore):N0} PTS";

        int seconds = Mathf.FloorToInt(MoveBird.SurvivalTime);
        if (survivalTimeText != null) survivalTimeText.text = $"{seconds / 60:D2}:{seconds % 60:D2}";

        UpdateCountdownLabel();
        PlayResultSound(victory);
    }

    private void ApplyTheme(bool victory, bool isNewRecord)
    {
        Color accent = victory ? victoryAccent : defeatAccent;
        Color subPanel = victory ? victorySubPanel : defeatSubPanel;

        // statusText ne sert qu'à l'annonce de nouveau record : masqué le reste du temps.
        if (statusText != null)
        {
            statusText.gameObject.SetActive(isNewRecord);
            if (isNewRecord)
            {
                statusText.text = "NOUVEAU RECORD !";
                statusText.color = newRecordColor;
                if (statusText.GetComponent<PulseEffect>() == null) statusText.gameObject.AddComponent<PulseEffect>();

                // Confettis en UI (et non ParticleBurst, rendu par la caméra donc toujours
                // derrière un Canvas Overlay) : garantis au premier plan, autour du texte.
                UIConfettiBurst.SpawnBurst(statusText.rectTransform);
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
            restartHintText.text = allowInstantRestartKeys ? "ESPACE POUR REJOUER" : "PRÉPAREZ LE PROCHAIN PILOTE";
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

    private void UpdateCountdownLabel()
    {
        if (!autoRestart)
        {
            if (countdownText != null) countdownText.text = allowInstantRestartKeys ? "ESPACE POUR CONTINUER" : "";
            if (countdownProgress != null) countdownProgress.fillAmount = 0f;
            return;
        }

        if (countdownText != null) countdownText.text = $"NOUVEAU VOL DANS {Mathf.Max(0, Mathf.CeilToInt(countdownTimer))}s";
        if (countdownProgress != null) countdownProgress.fillAmount = Mathf.Clamp01(countdownTimer / autoResetDelay);
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

    private static bool IsRestartKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
               (keyboard.spaceKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame);
    }

    // ------------------------------------------------------------------ prévisualisation (Play mode)

    // Clic droit sur le composant > "Debug : ..." pour voir un écran sans rejouer une partie.
    [ContextMenu("Debug : Afficher Nouveau Record")]
    private void DebugShowNewRecord() => DebugPreview(victory: false, newRecord: true);

    [ContextMenu("Debug : Afficher Victoire")]
    private void DebugShowVictory() => DebugPreview(victory: true, newRecord: false);

    [ContextMenu("Debug : Afficher Victoire + Nouveau Record")]
    private void DebugShowVictoryNewRecord() => DebugPreview(victory: true, newRecord: true);

    private void DebugPreview(bool victory, bool newRecord)
    {
        if (rootPanel == null) return;
        isGameOverActive = false;
        ShowResult(victory, newRecord);
    }
}
