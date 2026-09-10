using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KiBird.MainMenu
{
    /// <summary>
    /// Écran de démarrage. Le menu vit dans la même scène que le jeu : il fige l'oiseau pendant
    /// que le décor tourne déjà, puis le relâche quand la posture bras tendus a été tenue
    /// holdDurationToStart secondes — au clavier (Espace) ou devant la Kinect, indifféremment.
    /// Tant qu'aucun joueur n'est détecté, affiche une vidéo d'attente et masque le menu principal.
    /// Dès qu'un joueur est détecté, affiche le menu principal et masque la vidéo.
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
        [SerializeField] private Text countdownText;
        [SerializeField] private Text trackingStatusText;
        [SerializeField] private Image trackingStatusDot;

        [Header("Démarrage")]
        [SerializeField] private Image startProgressFill;
        [SerializeField] private float holdDurationToStart = 3f;
        [SerializeField] private GameObject menuVisualRoot;
        [SerializeField] private GameObject menuGameRoot;

        [Header("Attract Mode / Vidéo")]
        [Tooltip("Conteneur des éléments UI du menu principal (KiBirdLogo, Scores, Silhouette, etc.) affiché quand un joueur est détecté.")]
        [SerializeField] private GameObject menuContentRoot;
        [Tooltip("Conteneur de la vidéo d'attente affiché quand aucun joueur n'est détecté.")]
        [SerializeField] private GameObject videoRoot;
        [Tooltip("Délai de grâce en secondes avant de basculer sur la vidéo en cas de perte de suivi Kinect.")]
        [SerializeField] private float playerLostGraceDuration = 1.5f;
        [Tooltip("Force la détection d'un joueur (pratique en mode Éditeur pour déboguer sans Kinect, touche P).")]
        [SerializeField] private bool debugForcePlayerPresent = false;

        [Header("Couleurs de synchronisation")]
        [SerializeField] private Color waitingColor = new Color(0.76f, 0.68f, 0.48f);
        [SerializeField] private Color detectedColor = new Color(0.62f, 0.84f, 0.46f);
        [SerializeField] private Color chargingColor = new Color(1f, 0.73f, 0.16f);

        private string instructionMessage;

        private float holdTimer;
        private bool isStarting;
        private MoveBird birdMovement;

        private VideoPlayer cachedVideoPlayer;
        private float playerLostTimer;
        private bool isPlayerPresent;

        private void Start()
        {
            ResolveTrackingStatusReferences();
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

            if (menuContentRoot == null && menuVisualRoot != null)
            {
                Transform contentTransform = menuVisualRoot.transform.Find("Content");
                if (contentTransform != null) menuContentRoot = contentTransform.gameObject;
            }

            if (videoRoot == null && menuVisualRoot != null)
            {
                Transform videoTransform = menuVisualRoot.transform.Find("VideoPlayer");
                if (videoTransform != null) videoRoot = videoTransform.gameObject;
            }

            if (videoRoot != null)
            {
                cachedVideoPlayer = videoRoot.GetComponentInChildren<VideoPlayer>(true);
            }

            instructionMessage = promptText != null ? promptText.text : string.Empty;

            // Détection initiale
            isPlayerPresent = CheckRawPlayerPresent();
            SetDisplayMode(isPlayerPresent);
        }

        /// <summary>
        /// Garde l'indicateur fonctionnel si ses references ont ete oubliees dans l'Inspector.
        /// La recherche reste limitee au menu et ne s'execute qu'une fois au demarrage.
        /// </summary>
        private void ResolveTrackingStatusReferences()
        {
            if (menuVisualRoot == null ||
                (trackingStatusText != null && trackingStatusDot != null)) return;

            if (trackingStatusText == null)
            {
                foreach (Text text in menuVisualRoot.GetComponentsInChildren<Text>(true))
                {
                    if (text.name != "StatusText") continue;
                    trackingStatusText = text;
                    break;
                }
            }

            if (trackingStatusDot == null)
            {
                foreach (Image image in menuVisualRoot.GetComponentsInChildren<Image>(true))
                {
                    if (image.name != "StatusDot") continue;
                    trackingStatusDot = image;
                    break;
                }
            }

            if (trackingStatusText == null)
            {
                Debug.LogWarning("[MenuController] StatusText introuvable dans le menu : " +
                                 "l'etat de detection ne pourra pas etre affiche.");
            }
        }

        private void Update()
        {
            if (isStarting) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            {
                debugForcePlayerPresent = !debugForcePlayerPresent;
            }
#else
            if (Input.GetKeyDown(KeyCode.P))
            {
                debugForcePlayerPresent = !debugForcePlayerPresent;
            }
#endif
#endif

            bool rawPlayerPresent = CheckRawPlayerPresent();

            if (rawPlayerPresent)
            {
                playerLostTimer = 0f;
                isPlayerPresent = true;
            }
            else
            {
                if (isPlayerPresent)
                {
                    playerLostTimer += Time.deltaTime;
                    if (playerLostTimer >= playerLostGraceDuration)
                    {
                        isPlayerPresent = false;
                    }
                }
            }

            SetDisplayMode(isPlayerPresent);

            if (!isPlayerPresent)
            {
                holdTimer = 0f;
                if (startProgressFill != null) startProgressFill.fillAmount = 0f;
                return;
            }

            // Deux façons indépendantes de charger le démarrage : Espace, ou pose bras en T.
            bool charging = (inputProvider != null && inputProvider.IsGlideHeld) ||
                            (kinectInputProvider != null && kinectInputProvider.IsGlideHeld);

            UpdateStartProgress(charging);
            UpdatePrompt(charging);
            UpdateTrackingStatus(true, charging);
        }

        private bool CheckRawPlayerPresent()
        {
            if (debugForcePlayerPresent) return true;

            bool charging = (inputProvider != null && inputProvider.IsGlideHeld) ||
                            (kinectInputProvider != null && kinectInputProvider.IsGlideHeld);

            bool kinectHasPlayer = KinectInputSource.Instance != null && KinectInputSource.Instance.HasPlayer;

            return charging || kinectHasPlayer;
        }

        private void SetDisplayMode(bool playerDetected)
        {
            if (videoRoot != null)
            {
                bool shouldShowVideo = !playerDetected;
                if (videoRoot.activeSelf != shouldShowVideo)
                {
                    videoRoot.SetActive(shouldShowVideo);
                }

                if (cachedVideoPlayer != null)
                {
                    if (shouldShowVideo)
                    {
                        if (!cachedVideoPlayer.isPlaying) cachedVideoPlayer.Play();
                    }
                    else
                    {
                        if (cachedVideoPlayer.isPlaying) cachedVideoPlayer.Pause();
                    }
                }
            }

            if (menuContentRoot != null)
            {
                if (menuContentRoot.activeSelf != playerDetected)
                {
                    menuContentRoot.SetActive(playerDetected);
                }
            }
        }

        private void UpdatePrompt(bool charging)
        {
            Text dynamicText = countdownText != null ? countdownText : promptText;
            if (dynamicText == null) return;

            if (charging)
            {
                int remaining = Mathf.Clamp(Mathf.CeilToInt(holdDurationToStart - holdTimer), 1,
                    Mathf.CeilToInt(holdDurationToStart));
                dynamicText.text = remaining.ToString();
            }
            else
            {
                dynamicText.text = countdownText != null ? string.Empty : instructionMessage;
            }
        }

        private void UpdateTrackingStatus(bool playerPresent, bool charging)
        {
            if (trackingStatusText == null && trackingStatusDot == null) return;

            string message;
            Color color;
            if (charging)
            {
                message = "GARDEZ LA POSE";
                color = chargingColor;
            }
            else if (playerPresent)
            {
                message = "JOUEUR DÉTECTÉ";
                color = detectedColor;
            }
            else
            {
                message = "EN ATTENTE D'UN JOUEUR";
                color = waitingColor;
            }

            if (trackingStatusText != null)
            {
                trackingStatusText.text = message;
                trackingStatusText.color = color;
            }
            if (trackingStatusDot != null) trackingStatusDot.color = color;
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
            if (cachedVideoPlayer != null && cachedVideoPlayer.isPlaying)
            {
                cachedVideoPlayer.Stop();
            }
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
