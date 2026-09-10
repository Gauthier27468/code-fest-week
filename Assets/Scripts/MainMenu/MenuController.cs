using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Écran de démarrage. Le menu vit dans la même scène que le jeu : il fige l'oiseau pendant
    /// que le décor tourne déjà, puis le relâche quand la posture bras tendus a été tenue
    /// holdDurationToStart secondes — au clavier (Espace) ou devant la Kinect, indifféremment.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private DemoKeyboardInput inputProvider;
        [SerializeField] private KinectStartInputSource kinectInputProvider;
        [SerializeField] private SilhouetteRig playerSilhouette;

        [Header("Texte")]
        [Tooltip("Texte affichant le score du dernier vol.")]
        [SerializeField] private Text lastScoreText;
        [Tooltip("Texte affichant le meilleur score (Best Score).")]
        [SerializeField] private Text bestScoreText;
        [Tooltip("Texte affichant le classement des meilleurs scores du poste.")]
        [SerializeField] private Text leaderboardText;
        [SerializeField] private Text promptText;

        [Header("Démarrage")]
        [SerializeField] private Image startProgressFill;
        [SerializeField] private float holdDurationToStart = 3f;
        [SerializeField] private GameObject menuVisualRoot;
        [SerializeField] private GameObject menuGameRoot;

        private string instructionMessage;
        private float holdTimer;
        private bool isStarting;
        private MoveBird birdMovement;

        public void Configure(DemoKeyboardInput input, KinectStartInputSource kinectInput,
            SilhouetteRig silhouette, Text lastScore, Text bestScore, Text leaderboard, Text prompt,
            Image progressFill, GameObject visualRoot, GameObject gameRoot = null)
        {
            inputProvider = input;
            kinectInputProvider = kinectInput;
            playerSilhouette = silhouette;
            lastScoreText = lastScore;
            bestScoreText = bestScore;
            leaderboardText = leaderboard;
            promptText = prompt;
            startProgressFill = progressFill;
            menuVisualRoot = visualRoot;
            if (gameRoot != null) menuGameRoot = gameRoot;
        }

        private void Start()
        {
            RefreshScores();

            if (playerSilhouette != null) playerSilhouette.ApplyGlidePose();

            birdMovement = Object.FindFirstObjectByType<MoveBird>();
            if (birdMovement != null)
            {
                birdMovement.enabled = false;
            }
            else
            {
                Debug.LogWarning("[MenuController] Aucun MoveBird trouvé dans la scène : " +
                                 "l'oiseau ne sera pas bloqué pendant le menu.");
            }

            if (menuVisualRoot != null) menuVisualRoot.SetActive(true);
            if (menuGameRoot != null) menuGameRoot.SetActive(false);

            if (promptText != null) instructionMessage = promptText.text;
        }

        private void Update()
        {
            if (isStarting) return;

            // Deux façons indépendantes de charger le démarrage : Espace, ou pose bras en T.
            bool charging = (inputProvider != null && inputProvider.IsGlideHeld) ||
                            (kinectInputProvider != null && kinectInputProvider.IsGlideHeld);

            UpdateStartProgress(charging);
            UpdatePrompt(charging);
        }

        private void UpdatePrompt(bool charging)
        {
            if (promptText == null) return;

            if (charging)
            {
                int remaining = Mathf.Clamp(Mathf.CeilToInt(holdDurationToStart - holdTimer), 1,
                    Mathf.CeilToInt(holdDurationToStart));
                promptText.text = remaining + "...";
            }
            else
            {
                promptText.text = instructionMessage;
            }
        }

        private void UpdateStartProgress(bool charging)
        {
            holdTimer = charging ? holdTimer + Time.deltaTime : 0f;

            float progress = Mathf.Clamp01(holdTimer / holdDurationToStart);
            if (startProgressFill != null) startProgressFill.fillAmount = progress;

            if (progress >= 1f)
            {
                StartGame();
            }
        }

        private void StartGame()
        {
            isStarting = true;
            if (menuVisualRoot != null) menuVisualRoot.SetActive(false);
            if (menuGameRoot != null) menuGameRoot.SetActive(true);

            if (birdMovement != null)
            {
                birdMovement.enabled = true;
                birdMovement.PlayMainMusic();
            }
        }

        private void RefreshScores()
        {
            if (lastScoreText != null)
            {
                lastScoreText.text = ScoreManager.GetLastScore().ToString();
            }

            if (bestScoreText != null)
            {
                bestScoreText.text = ScoreManager.GetBestScore().ToString();
            }

            if (leaderboardText != null)
            {
                List<int> scores = ScoreManager.GetTopScores();
                if (scores.Count == 0)
                {
                    leaderboardText.text = "—";
                    return;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < scores.Count; i++)
                {
                    sb.AppendLine($"{i + 1}.  {scores[i]}");
                }
                leaderboardText.text = sb.ToString();
            }
        }
    }
}
