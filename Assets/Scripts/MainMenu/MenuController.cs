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
        [SerializeField] private SilhouetteRig playerSilhouette;

        [Header("Texte")]
        [SerializeField] private Text lastScoreText;
        [SerializeField] private Text leaderboardText;
        [SerializeField] private Text promptText;

        [Header("Démarrage")]
        [SerializeField] private Image startProgressFill;
        [SerializeField] private float holdDurationToStart = 3f;
        [SerializeField] private GameObject menuVisualRoot;

        private const string InstructionMessage = "Tendez les bras pendant 3 secondes pour lancer le jeu.";

        private float holdTimer;
        private bool isStarting;
        private MoveBird birdMovement;

        public void Configure(DemoKeyboardInput input, SilhouetteRig silhouette, Text lastScore,
            Text leaderboard, Text prompt, Image progressFill, GameObject visualRoot)
        {
            inputProvider = input;
            playerSilhouette = silhouette;
            lastScoreText = lastScore;
            leaderboardText = leaderboard;
            promptText = prompt;
            startProgressFill = progressFill;
            menuVisualRoot = visualRoot;
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
            if (birdMovement != null) birdMovement.enabled = false;
        }

        private void Update()
        {
            if (isStarting) return;

            bool present = inputProvider.IsPlayerPresent;
            BirdPose pose = inputProvider.CurrentPose;
            bool charging = present && pose == BirdPose.Glide;

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
        }

        private void RefreshScores()
        {
            lastScoreText.text = ScoreManager.GetLastScore().ToString();

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
