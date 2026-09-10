using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Écran de fin de partie : score, temps de survie, décompte puis rechargement automatique
/// de la scène pour le joueur suivant. Espace / R / Entrée relancent tout de suite.
/// Les références UI sont câblées dans la scène ; celles laissées vides sont retrouvées par nom.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [Header("Paramètres de Réinitialisation")]
    [Tooltip("Délai en secondes avant le redémarrage automatique de la scène.")]
    public float autoResetDelay = 5f;

    [Tooltip("Permet de redémarrer immédiatement avec la touche Espace ou R.")]
    public bool allowInstantRestartKeys = true;

    [Header("Éléments d'Interface")]
    public GameObject rootPanel;
    public Text titleText;
    public Text scoreText;
    public Text survivalTimeText;
    public Text countdownText;
    public Image backgroundOverlay;
    public Image cardBg;

    [Header("Sons de Fin de Partie")]
    public AudioSource gameOverSound;
    public AudioClip defeatSound;
    public AudioClip victorySound;

    private float countdownTimer;
    private bool isGameOverActive;
    private bool isVictory;

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

        ResolveReferences();

        if (rootPanel != null)
        {
            rootPanel.SetActive(false);
        }
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

    private void HandleBirdDied()
    {
        ShowGameOver(false);
    }

    private void HandleBirdWon()
    {
        ShowVictory();
    }

    public void ShowVictory()
    {
        ShowGameOver(true);
    }

    public void ShowGameOver(bool victory = false)
    {
        if (isGameOverActive) return;
        isGameOverActive = true;
        isVictory = victory;
        countdownTimer = autoResetDelay;

        ResolveReferences();

        if (rootPanel != null)
        {
            rootPanel.SetActive(true);
        }

        if (isVictory)
        {
            ApplyVictoryTheme();
        }
        else
        {
            ApplyDefeatTheme();
        }

        if (scoreText != null)
        {
            string label = isVictory ? "SCORE FINAL" : "SCORE";
            scoreText.text = $"{label} : {MoveBird.CurrentScore:N0} PTS";
        }

        if (survivalTimeText != null)
        {
            int totalSeconds = Mathf.FloorToInt(MoveBird.SurvivalTime);
            string label = isVictory ? "TEMPS DE VOL" : "TEMPS DE SURVIE";
            survivalTimeText.text = $"{label} : {totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
        }

        UpdateCountdownLabel();
    }

    private void ApplyVictoryTheme()
    {
        if (titleText != null)
        {
            titleText.text = "VICTOIRE !";
            titleText.color = new Color(0.2f, 0.95f, 0.45f);
        }

        if (cardBg != null)
        {
            cardBg.color = new Color(0.04f, 0.22f, 0.12f, 0.96f);
        }

        if (backgroundOverlay != null)
        {
            backgroundOverlay.color = new Color(0.01f, 0.10f, 0.05f, 0.88f);
        }

        PlayEndSound(victorySound);
    }

    private void ApplyDefeatTheme()
    {
        if (titleText != null)
        {
            titleText.text = "GAME OVER";
            titleText.color = new Color(1f, 0.85f, 0.15f);
        }

        if (cardBg != null)
        {
            cardBg.color = new Color(0.07f, 0.14f, 0.25f, 0.95f);
        }

        if (backgroundOverlay != null)
        {
            backgroundOverlay.color = new Color(0.04f, 0.08f, 0.15f, 0.88f);
        }

        PlayEndSound(defeatSound);
    }

    private void PlayEndSound(AudioClip clip)
    {
        if (gameOverSound == null) return;

        gameOverSound.Stop();
        if (clip != null)
        {
            gameOverSound.PlayOneShot(clip);
        }
        else
        {
            gameOverSound.Play();
        }
    }

    private void Update()
    {
        if (!isGameOverActive) return;

        countdownTimer -= Time.unscaledDeltaTime;
        UpdateCountdownLabel();

        if (allowInstantRestartKeys && IsRestartKeyPressed())
        {
            RestartScene();
            return;
        }

        if (countdownTimer <= 0f)
        {
            RestartScene();
        }
    }

    private void UpdateCountdownLabel()
    {
        if (countdownText == null) return;

        int remaining = Mathf.Max(1, Mathf.CeilToInt(countdownTimer));
        countdownText.text = $"Nouvelle partie dans {remaining}s...";
    }

    private static bool IsRestartKeyPressed()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.spaceKey.wasPressedThisFrame ||
                              kb.rKey.wasPressedThisFrame ||
                              kb.enterKey.wasPressedThisFrame);
    }

    public void RestartScene()
    {
        isGameOverActive = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Retrouve par nom les références UI laissées vides dans l'inspecteur.</summary>
    private void ResolveReferences()
    {
        if (rootPanel == null)
        {
            Transform panelTr = transform.Find("GameOverMenu") ?? transform.Find("GameOverPanel");
            if (panelTr != null) rootPanel = panelTr.gameObject;
        }

        if (rootPanel == null) return;

        if (titleText == null) titleText = FindText("Title", "GAME OVER", "VICTOIRE");
        if (scoreText == null) scoreText = FindText("Score");
        if (survivalTimeText == null) survivalTimeText = FindText("Time", "Survival");
        if (countdownText == null) countdownText = FindText("Countdown");

        if (cardBg == null)
        {
            foreach (var img in rootPanel.GetComponentsInChildren<Image>(true))
            {
                if (img.name.IndexOf("Card", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cardBg = img;
                    break;
                }
            }
        }

        if (backgroundOverlay == null)
        {
            Transform bgTr = rootPanel.transform.Find("BackgroundOverlay");
            backgroundOverlay = bgTr != null ? bgTr.GetComponent<Image>() : rootPanel.GetComponent<Image>();
        }
    }

    /// <summary>Premier Text dont le nom ou le contenu contient l'un des motifs donnés.</summary>
    private Text FindText(params string[] patterns)
    {
        foreach (var txt in rootPanel.GetComponentsInChildren<Text>(true))
        {
            foreach (string pattern in patterns)
            {
                if (txt.name.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.text.Contains(pattern))
                {
                    return txt;
                }
            }
        }
        return null;
    }
}
