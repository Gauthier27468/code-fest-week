using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace KiBird.MainMenu
{
    // Orchestre l'écran de démarrage : anime la silhouette du joueur en fonction de
    // l'entrée courante, affiche le message d'invite et gère le "maintien de la pose"
    // pour démarrer une partie. Le menu vit dans la même scène que le jeu (Blocks.unity) :
    // au démarrage il fige l'oiseau (MoveBird désactivé) pendant que le reste du jeu tourne
    // déjà normalement, puis à la fin du décompte il se masque et relâche l'oiseau.
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
        [Tooltip("Titre affiché au-dessus du score (ex: BEST SCORE).")]
        [SerializeField] private Text scoreTitleText;
        [SerializeField] private Text leaderboardText;
        [SerializeField] private Text promptText;

        [Header("Démarrage")]
        [SerializeField] private Image startProgressFill;
        [SerializeField] private float holdDurationToStart = 3f;
        [SerializeField] private GameObject menuVisualRoot;
        [SerializeField] private GameObject menuGameRoot;

        private string InstructionMessage;

        private float holdTimer;
        private bool isStarting;
        private MoveBird birdMovement;

        public void Configure(DemoKeyboardInput input, KinectStartInputSource kinectInput,
            SilhouetteRig silhouette, Text lastScore, Text bestScore, Text prompt, Image progressFill,
            GameObject visualRoot, GameObject gameRoot = null)
        {
            inputProvider = input;
            kinectInputProvider = kinectInput;
            playerSilhouette = silhouette;
            lastScoreText = lastScore;
            bestScoreText = bestScore;
            promptText = prompt;
            startProgressFill = progressFill;
            menuVisualRoot = visualRoot;
            if (gameRoot != null) menuGameRoot = gameRoot;
        }

        public void Configure(DemoKeyboardInput input, KinectStartInputSource kinectInput,
            SilhouetteRig silhouette, Text lastScore, Text leaderboard, Text prompt, Image progressFill,
            GameObject visualRoot)
        {
            Configure(input, kinectInput, silhouette, lastScore, null, prompt, progressFill, visualRoot);
            leaderboardText = leaderboard;
        }

        private void Start()
        {
            RefreshScores();
            // Le personnage central est un badge décoratif : il reste toujours en pose T
            // (bras à l'horizontale), seul l'anneau de progression réagit à la pose tenue.
            playerSilhouette.SetPoseInstant(BirdPose.Glide);

            // Le jeu tourne déjà (blocs, hoops, environnement...) mais l'oiseau reste immobile
            // tant que le menu n'a pas laissé la main.
            birdMovement = Object.FindFirstObjectByType<MoveBird>();
            if (birdMovement != null)
            {
                birdMovement.enabled = false;
            }
            else
            {
                // Si ça ne trouve rien, l'oiseau reste piloté dès le chargement de la scène :
                // symptôme typique d'un "démarrage automatique" alors que le menu est affiché.
                Debug.LogWarning("[MenuController] Aucun MoveBird trouvé dans la scène : " +
                                  "l'oiseau ne sera pas bloqué pendant le menu.");
            }

            // Le menu visuel est actif, le menu de jeu (score, temps de survie) est masqué.
            if (menuVisualRoot != null) menuVisualRoot.SetActive(true);
            if (menuGameRoot != null) menuGameRoot.SetActive(false);

            InstructionMessage = promptText.text;
        }

        private void Update()
        {
            if (isStarting) return;

            // Deux façons indépendantes de démarrer : clavier (Espace, debug/animateur) ou pose
            // "bras en T" devant la Kinect. Il suffit que l'une des deux soit tenue 3 secondes.
            bool keyboardCharging = inputProvider != null && inputProvider.IsPlayerPresent &&
                                     inputProvider.CurrentPose == BirdPose.Glide;
            bool kinectCharging = kinectInputProvider != null && kinectInputProvider.IsPlayerPresent &&
                                   kinectInputProvider.CurrentPose == BirdPose.Glide;
            bool charging = keyboardCharging || kinectCharging;

            UpdateStartProgress(charging);
            UpdatePrompt(charging);
        }

        // Tant que le joueur ne tend pas les bras : instruction fixe.
        // Dès qu'il tend les bras (pose T) : décompte "3...", "2...", "1..." jusqu'au lancement.
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
                promptText.text = InstructionMessage;
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
            // Exactement à la fin des 3 secondes tenues : on relâche l'oiseau et on masque le
            // menu tout de suite, pas de délai supplémentaire ni de changement de scène.
            if (birdMovement != null) birdMovement.enabled = true;
            if (menuVisualRoot != null) menuVisualRoot.SetActive(false);
            if (menuGameRoot != null) menuGameRoot.SetActive(true);

            // Start bird music !
            birdMovement.PlayMainMusic();
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
