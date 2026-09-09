using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Gestionnaire et affichage de l'écran Game Over (KiBird).
/// - Affiche le score et le temps de survie de la partie.
/// - Décompte 5 secondes puis recharge automatiquement la scène pour le joueur suivant.
/// - Permet aussi un redémarrage instantané via Espace ou R (pour débug/animateur).
/// - Construit automatiquement une interface visuelle propre si aucun Canvas n'est assigné.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [Header("Paramètres de Réinitialisation")]
    [Tooltip("Délai en secondes avant le redémarrage automatique de la scène.")]
    public float autoResetDelay = 5f;

    [Tooltip("Permet de redémarrer immédiatement avec la touche Espace ou R.")]
    public bool allowInstantRestartKeys = true;

    [Header("Police de Caractères")]
    public Font customFont;

    [Header("Éléments d'Interface (Optionnels - auto-créés si vides)")]
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
    private bool isGameOverActive = false;
    private bool isVictory = false;

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

        if (customFont == null)
        {
#if UNITY_EDITOR
            customFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/LuckiestGuy-Regular.ttf");
#endif
            if (customFont == null)
            {
                customFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        if (victorySound == null)
        {
#if UNITY_EDITOR
            victorySound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AssetStore/Sounds/victory.wav");
#endif
        }

        EnsureUIExists();

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

    /// <summary>
    /// Affiche l'écran de victoire en vert avec le son triomphal et le score final.
    /// </summary>
    public void ShowVictory()
    {
        ShowGameOver(true);
    }

    /// <summary>
    /// Affiche l'écran de fin de partie (Victoire ou Défaite).
    /// </summary>
    public void ShowGameOver(bool isVictory = false)
    {
        if (isGameOverActive) return;
        isGameOverActive = true;
        this.isVictory = isVictory;
        countdownTimer = autoResetDelay;

        ResolveReferences();
        EnsureUIExists();

        if (rootPanel != null)
        {
            rootPanel.SetActive(true);
        }

        // Palette de couleurs & Sons : Vert pour la victoire, Bleu/Orange pour la défaite
        if (isVictory)
        {
            if (titleText != null)
            {
                titleText.text = "VICTOIRE !";
                titleText.color = new Color(0.2f, 0.95f, 0.45f); // Vert émeraude éclatant
            }

            if (cardBg != null)
            {
                cardBg.color = new Color(0.04f, 0.22f, 0.12f, 0.96f); // Fond carte vert forêt
            }
            else if (rootPanel != null)
            {
                foreach (var img in rootPanel.GetComponentsInChildren<Image>(true))
                {
                    if (img != backgroundOverlay)
                    {
                        img.color = new Color(0.04f, 0.22f, 0.12f, 0.96f);
                    }
                }
            }

            if (backgroundOverlay != null)
            {
                backgroundOverlay.color = new Color(0.01f, 0.10f, 0.05f, 0.88f);
            }

            // Lecture du son de victoire
            AudioClip clip = victorySound;
            if (clip == null)
            {
#if UNITY_EDITOR
                clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AssetStore/Sounds/victory.wav");
#endif
            }

            if (clip != null)
            {
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
        }
        else
        {
            if (titleText != null)
            {
                titleText.text = "GAME OVER";
                titleText.color = new Color(1f, 0.85f, 0.15f); // Jaune / orange
            }

            if (cardBg != null)
            {
                cardBg.color = new Color(0.07f, 0.14f, 0.25f, 0.95f); // Fond carte bleu nuit
            }

            if (backgroundOverlay != null)
            {
                backgroundOverlay.color = new Color(0.04f, 0.08f, 0.15f, 0.88f);
            }

            // Lecture du son Game Over standard
            AudioClip clip = defeatSound;
            if (clip == null && gameOverSound != null && gameOverSound.clip != null)
            {
                clip = gameOverSound.clip;
            }

            if (clip != null && gameOverSound != null)
            {
                gameOverSound.Stop();
                gameOverSound.PlayOneShot(clip);
            }
            else if (gameOverSound != null)
            {
                gameOverSound.Play();
            }
        }

        // Formatage du score
        if (scoreText != null)
        {
            string label = isVictory ? "SCORE FINAL" : "SCORE";
            scoreText.text = $"{label} : {MoveBird.CurrentScore:N0} PTS";
        }

        // Formatage du temps de vol / survie
        if (survivalTimeText != null)
        {
            int totalSeconds = Mathf.FloorToInt(MoveBird.SurvivalTime);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            string label = isVictory ? "TEMPS DE VOL" : "TEMPS DE SURVIE";
            survivalTimeText.text = $"{label} : {minutes:D2}:{seconds:D2}";
        }

        UpdateCountdownLabel();
    }

    private void Update()
    {
        if (!isGameOverActive) return;

        countdownTimer -= Time.unscaledDeltaTime;
        UpdateCountdownLabel();

        // Raccourci pour redémarrer immédiatement sans attendre les 5 secondes
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
        if (countdownText != null)
        {
            int remaining = Mathf.Max(1, Mathf.CeilToInt(countdownTimer));
            string label = isVictory ? "Nouvelle partie dans" : "Nouvelle partie dans";
            countdownText.text = $"{label} {remaining}s...";
        }
    }

    private bool IsRestartKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return))
        {
            return true;
        }
#endif
        return false;
    }

    public void RestartScene()
    {
        isGameOverActive = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>
    /// Résout automatiquement les références UI si elles n'ont pas été assignées dans l'inspecteur.
    /// </summary>
    private void ResolveReferences()
    {
        if (rootPanel == null)
        {
            Transform panelTr = transform.Find("GameOverMenu") ?? transform.Find("GameOverPanel");
            if (panelTr != null) rootPanel = panelTr.gameObject;
        }

        if (rootPanel != null)
        {
            if (titleText == null)
            {
                foreach (var txt in rootPanel.GetComponentsInChildren<Text>(true))
                {
                    if (txt.name.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.text.Contains("GAME OVER") || txt.text.Contains("VICTOIRE"))
                    {
                        titleText = txt;
                        break;
                    }
                }
            }

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
                if (bgTr != null)
                {
                    backgroundOverlay = bgTr.GetComponent<Image>();
                }
                if (backgroundOverlay == null)
                {
                    backgroundOverlay = rootPanel.GetComponent<Image>();
                }
            }
            else if (cardBg == null && backgroundOverlay.name.IndexOf("Card", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cardBg = backgroundOverlay;
                backgroundOverlay = rootPanel.GetComponent<Image>();
            }

            if (scoreText == null)
            {
                foreach (var txt in rootPanel.GetComponentsInChildren<Text>(true))
                {
                    if (txt.name.IndexOf("Score", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        scoreText = txt;
                        break;
                    }
                }
            }

            if (survivalTimeText == null)
            {
                foreach (var txt in rootPanel.GetComponentsInChildren<Text>(true))
                {
                    if (txt.name.IndexOf("Time", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.name.IndexOf("Survival", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        survivalTimeText = txt;
                        break;
                    }
                }
            }

            if (countdownText == null)
            {
                foreach (var txt in rootPanel.GetComponentsInChildren<Text>(true))
                {
                    if (txt.name.IndexOf("Countdown", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        countdownText = txt;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Construit dynamiquement l'arborescence UI complète si l'utilisateur n'a pas câblé de Canvas pré-existant.
    /// </summary>
    private void EnsureUIExists()
    {
        ResolveReferences();

        if (rootPanel != null && scoreText != null && survivalTimeText != null && countdownText != null)
        {
            return;
        }

        // Recherche d'un Canvas existant ou création d'un Canvas dédié
        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("GameOverCanvas");
            canvasGO.transform.SetParent(transform, false);

            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Au-dessus de tous les autres menus

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // Fond sombre translucide plein écran
        if (rootPanel == null)
        {
            GameObject panelGO = new GameObject("GameOverPanel");
            panelGO.transform.SetParent(canvas.transform, false);

            RectTransform panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            backgroundOverlay = panelGO.AddComponent<Image>();
            backgroundOverlay.color = new Color(0.04f, 0.08f, 0.15f, 0.88f);

            rootPanel = panelGO;
        }

        // Boîte centrale stylisée
        GameObject cardGO = new GameObject("GameOverCard");
        cardGO.transform.SetParent(rootPanel.transform, false);

        RectTransform cardRT = cardGO.AddComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(750, 480);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;

        if (cardBg == null)
        {
            cardBg = cardGO.AddComponent<Image>();
            cardBg.color = new Color(0.07f, 0.14f, 0.25f, 0.95f);
        }

        // Titre "GAME OVER"
        if (titleText == null)
        {
            titleText = CreateTextElement(cardGO.transform, "GameOverTitle", "GAME OVER", 64, new Color(1f, 0.85f, 0.15f), new Vector2(0, 150), new Vector2(700, 80));
        }

        // Score
        if (scoreText == null)
        {
            scoreText = CreateTextElement(cardGO.transform, "ScoreText", "SCORE : 0 PTS", 44, Color.white, new Vector2(0, 50), new Vector2(700, 60));
        }

        // Temps de survie
        if (survivalTimeText == null)
        {
            survivalTimeText = CreateTextElement(cardGO.transform, "SurvivalTimeText", "TEMPS DE SURVIE : 00:00", 34, new Color(0.78f, 0.93f, 0.82f), new Vector2(0, -20), new Vector2(700, 50));
        }

        // Décompte reset (5s)
        if (countdownText == null)
        {
            countdownText = CreateTextElement(cardGO.transform, "CountdownText", "Nouvelle partie dans 5s...", 30, new Color(0.3f, 0.85f, 1f), new Vector2(0, -90), new Vector2(700, 50));
        }

        // Info touche redémarrage
        CreateTextElement(cardGO.transform, "RestartHint", "(Appuyez sur Espace pour relancer tout de suite)", 20, new Color(0.7f, 0.7f, 0.7f, 0.8f), new Vector2(0, -170), new Vector2(700, 40));
    }

    private Text CreateTextElement(Transform parent, string name, string text, int fontSize, Color color, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Text txt = go.AddComponent<Text>();
        txt.font = customFont;
        txt.text = text;
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = color;

        // Contour doux pour la lisibilité
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(2f, -2f);

        return txt;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("KiBird/Ajouter GameOverUI dans la Scène Active")]
    public static void AddGameOverUIToScene()
    {
        GameOverUI existing = Object.FindAnyObjectByType<GameOverUI>();
        if (existing != null)
        {
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            Debug.Log("[KiBird] GameOverUI existe déjà dans la scène.");
            return;
        }

        GameObject go = new GameObject("GameOverManager");
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create GameOverUI");

        GameOverUI ui = go.AddComponent<GameOverUI>();
        UnityEditor.Selection.activeGameObject = go;
        Debug.Log("[KiBird] GameOverUI créé avec succès dans la scène active !");
    }
#endif
}
