using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;

namespace KiBird.MainMenu
{
    /// <summary>
    /// Écran de démarrage. Le menu vit dans la même scène que le jeu : il fige l'oiseau pendant
    /// que le décor tourne déjà, puis le relâche quand la posture bras tendus a été tenue
    /// holdDurationToStart secondes — au clavier (Espace) ou devant la Kinect, indifféremment.
    /// Sans joueur détecté, une vidéo d'attente remplace le menu.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private DemoKeyboardInput inputProvider;
        [SerializeField] private KinectStartInputSource kinectInputProvider;

        [Header("Texte")]
        [Tooltip("Texte affichant le score du dernier vol.")]
        [SerializeField] private Text lastScoreText;
        [Tooltip("Texte affichant le meilleur score (Best Score).")]
        [SerializeField] private Text bestScoreText;
        [SerializeField] private Text promptText;
        [Tooltip("Optionnel : sinon le compte à rebours s'affiche dans promptText.")]
        [SerializeField] private Text countdownText;
        [SerializeField] private Text trackingStatusText;
        [SerializeField] private Image trackingStatusDot;

        [Header("Démarrage")]
        [SerializeField] private Image startProgressFill;
        [SerializeField] private float holdDurationToStart = 3f;
        [SerializeField] private GameObject menuVisualRoot;
        [SerializeField] private GameObject menuGameRoot;

        [Header("Attract Mode / Vidéo")]
        [Tooltip("Contenu du menu principal (logo, scores...) affiché quand un joueur est détecté.")]
        [SerializeField] private GameObject menuContentRoot;
        [Tooltip("Vidéo d'attente affichée quand aucun joueur n'est détecté.")]
        [SerializeField] private GameObject videoRoot;
        [Tooltip("Délai de grâce en secondes avant de rebasculer sur la vidéo après une perte de suivi.")]
        [SerializeField] private float playerLostGraceDuration = 1.5f;
        [Tooltip("Force la détection d'un joueur, pour déboguer sans Kinect (touche P en éditeur).")]
        [SerializeField] private bool debugForcePlayerPresent = false;

        [Header("Couleurs de synchronisation")]
        [SerializeField] private Color detectedColor = new Color(0.62f, 0.84f, 0.46f);
        [SerializeField] private Color chargingColor = new Color(1f, 0.73f, 0.16f);

        private string instructionMessage;
        private float holdTimer;
        private bool isStarting;
        private MoveBird birdMovement;

        private VideoPlayer videoPlayer;
        private float playerLostTimer;
        private bool isPlayerPresent;

        private void Start()
        {
            RefreshScores();

            // L'oiseau reste figé tant que la partie n'a pas démarré.
            birdMovement = FindAnyObjectByType<MoveBird>();
            if (birdMovement != null)
            {
                birdMovement.enabled = false;
            }
            else
            {
                Debug.LogWarning("[MenuController] Aucun MoveBird dans la scène : l'oiseau ne sera pas bloqué pendant le menu.");
            }

            if (menuVisualRoot != null) menuVisualRoot.SetActive(true);
            if (menuGameRoot != null) menuGameRoot.SetActive(false);
            if (videoRoot != null) videoPlayer = videoRoot.GetComponentInChildren<VideoPlayer>(true);

            instructionMessage = promptText != null ? promptText.text : string.Empty;

            isPlayerPresent = IsPlayerDetected(IsChargingStart());
            SetDisplayMode(isPlayerPresent);
        }

        private void Update()
        {
            if (isStarting) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            {
                debugForcePlayerPresent = !debugForcePlayerPresent;
            }
#endif

            bool charging = IsChargingStart();
            UpdatePlayerPresence(IsPlayerDetected(charging));
            SetDisplayMode(isPlayerPresent);

            if (!isPlayerPresent)
            {
                holdTimer = 0f;
                if (startProgressFill != null) startProgressFill.fillAmount = 0f;
                return;
            }

            UpdateStartProgress(charging);
            UpdatePrompt(charging);
            UpdateTrackingStatus(charging);
        }

        /// <summary>Deux façons indépendantes de charger le démarrage : Espace, ou pose bras en T.</summary>
        private bool IsChargingStart() =>
            (inputProvider != null && inputProvider.IsGlideHeld) ||
            (kinectInputProvider != null && kinectInputProvider.IsGlideHeld);

        private bool IsPlayerDetected(bool charging) =>
            debugForcePlayerPresent || charging ||
            (KinectInputSource.Instance != null && KinectInputSource.Instance.HasPlayer);

        /// <summary>Présence immédiate, mais disparition seulement après le délai de grâce.</summary>
        private void UpdatePlayerPresence(bool detected)
        {
            if (detected)
            {
                playerLostTimer = 0f;
                isPlayerPresent = true;
            }
            else if (isPlayerPresent)
            {
                playerLostTimer += Time.deltaTime;
                if (playerLostTimer >= playerLostGraceDuration) isPlayerPresent = false;
            }
        }

        /// <summary>Menu si un joueur est là, vidéo d'attente sinon.</summary>
        private void SetDisplayMode(bool playerDetected)
        {
            if (videoRoot != null)
            {
                bool showVideo = !playerDetected;
                if (videoRoot.activeSelf != showVideo) videoRoot.SetActive(showVideo);

                if (videoPlayer != null)
                {
                    if (showVideo && !videoPlayer.isPlaying) videoPlayer.Play();
                    else if (!showVideo && videoPlayer.isPlaying) videoPlayer.Pause();
                }
            }

            if (menuContentRoot != null && menuContentRoot.activeSelf != playerDetected)
            {
                menuContentRoot.SetActive(playerDetected);
            }
        }

        private void UpdateStartProgress(bool charging)
        {
            holdTimer = charging ? holdTimer + Time.deltaTime : 0f;

            float progress = Mathf.Clamp01(holdTimer / holdDurationToStart);
            if (startProgressFill != null) startProgressFill.fillAmount = progress;

            if (progress >= 1f) StartGame();
        }

        /// <summary>Compte à rebours pendant la pose, message d'instruction sinon.</summary>
        private void UpdatePrompt(bool charging)
        {
            Text label = countdownText != null ? countdownText : promptText;
            if (label == null) return;

            if (charging)
            {
                int remaining = Mathf.Clamp(Mathf.CeilToInt(holdDurationToStart - holdTimer), 1,
                    Mathf.CeilToInt(holdDurationToStart));
                label.text = remaining.ToString();
            }
            else
            {
                label.text = countdownText != null ? string.Empty : instructionMessage;
            }
        }

        private void UpdateTrackingStatus(bool charging)
        {
            Color color = charging ? chargingColor : detectedColor;

            if (trackingStatusText != null)
            {
                trackingStatusText.text = charging ? "GARDEZ LA POSE" : "JOUEUR DÉTECTÉ";
                trackingStatusText.color = color;
            }
            if (trackingStatusDot != null) trackingStatusDot.color = color;
        }

        private void StartGame()
        {
            isStarting = true;

            if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Stop();
            if (videoRoot != null) videoRoot.SetActive(false);
            if (menuContentRoot != null) menuContentRoot.SetActive(false);
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
            if (lastScoreText != null) lastScoreText.text = ScoreManager.GetLastScore().ToString();
            if (bestScoreText != null) bestScoreText.text = ScoreManager.GetBestScore().ToString();
        }
    }
}
