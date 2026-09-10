using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KiBird.UI
{
    /// <summary>
    /// Jauge de planage du HUD : montre en continu le pourcentage lu sur <see cref="MoveBird.GlideRatio"/>
    /// (la même valeur que la barre "Plané" de l'overlay F1), mais dans un habillage lisible par un
    /// visiteur à plusieurs mètres du vidéoprojecteur.
    ///
    /// Le composant fonctionne de deux façons :
    ///  - références câblées dans la hiérarchie (fillImage / percentText / ...) : le style vit dans
    ///    la scène, comme pour l'écran de fin de partie, et ce script se contente de l'animer ;
    ///  - références vides : la jauge se construit toute seule au démarrage, dans la palette bois
    ///    des menus et avec la police déjà utilisée par le score. Rien à préparer dans la scène.
    ///
    /// Le libellé n'est réécrit qu'au changement de pourcentage entier, et les chaînes sont
    /// pré-calculées : la jauge ne déclenche donc ni allocation ni rebuild uGUI par frame.
    /// </summary>
    [AddComponentMenu("KiBird/Jauge de planage")]
    public sealed class GlideGauge : MonoBehaviour
    {
        /// <summary>
        /// Mettre à false (depuis un RuntimeInitializeOnLoadMethod BeforeSceneLoad) pour empêcher
        /// la création automatique et poser la jauge soi-même dans la scène.
        /// </summary>
        public static bool AutoBootstrap = true;

        public static GlideGauge Instance { get; private set; }

        [Header("Références (laisser vide : la jauge se construit toute seule)")]
        [Tooltip("Canvas d'accueil. Si vide : le Canvas parent, sinon le premier Canvas de la scène.")]
        public Canvas targetCanvas;

        [Tooltip("Image de remplissage. En mode Filled, c'est fillAmount qui est piloté ; sinon la hauteur du rect.")]
        public Image fillImage;

        [Tooltip("Libellé du pourcentage (ex. « 85% »).")]
        public Text percentText;

        [Tooltip("Titre de la jauge (« PLANÉ »). Optionnel.")]
        public Text captionText;

        [Tooltip("Rail sombre derrière le remplissage. Optionnel, purement décoratif.")]
        public Image trackImage;

        [Header("Style (jauge auto-générée uniquement)")]
        [Tooltip("Police du HUD. Si vide, celle du score est réutilisée.")]
        public Font font;

        [Tooltip("Sprite bois optionnel pour le cadre (Assets/Art/UI/wood-dark-tile.png).")]
        public Sprite frameSprite;

        [Tooltip("Coin d'ancrage et marge, en pixels de la résolution de référence (1920x1080).")]
        public Vector2 margin = new Vector2(56f, 56f);

        [Tooltip("Taille de la jauge complète, libellés compris.")]
        public Vector2 size = new Vector2(112f, 430f);

        [Header("Couleurs")]
        [Tooltip("Piqué : bras le long du corps, l'oiseau descend vite.")]
        public Color diveColor = new Color(0.93f, 0.33f, 0.22f);

        [Tooltip("Planage partiel : bras à mi-hauteur.")]
        public Color midColor = new Color(1f, 0.73f, 0.16f);

        [Tooltip("Plané plein : bras tendus à l'horizontale, la posture à trouver.")]
        public Color glideColor = new Color(0.62f, 0.83f, 0.46f);

        [Header("Animation")]
        [Tooltip("Lissage de l'aiguille. Plus la valeur est basse, plus la jauge est calme.")]
        [Range(1f, 30f)] public float smoothing = 9f;

        [Tooltip("Pulse discret quand le plané est au maximum : signale au joueur qu'il a trouvé la bonne posture.")]
        public bool pulseOnFullGlide = true;

        [Tooltip("Seuil à partir duquel le plané est considéré comme plein.")]
        [Range(0.5f, 1f)] public float fullGlideThreshold = 0.92f;

        [Tooltip("Durée du fondu d'entrée et de sortie (fin de partie).")]
        [Min(0.05f)] public float fadeDuration = 0.35f;

        private static readonly Color Cream = new Color(1f, 0.94f, 0.75f);
        private static readonly Color WoodDark = new Color(0.20f, 0.095f, 0.035f, 0.95f);
        private static readonly Color TrackDark = new Color(0.08f, 0.05f, 0.03f, 0.88f);

        /// <summary>Chaînes « 0% » à « 100% », pour n'allouer aucune string en jeu.</summary>
        private static readonly string[] PercentStrings = BuildPercentStrings();

        private static string[] BuildPercentStrings()
        {
            var cache = new string[101];
            for (int i = 0; i < cache.Length; i++) cache[i] = i + "%";
            return cache;
        }

        private MoveBird bird;
        private CanvasGroup group;
        private RectTransform barRect;
        private RectTransform fillRect;
        private float displayedRatio = 1f;
        private int displayedPercent = -1;
        private float pulseTime;

        /// <summary>
        /// Crée la jauge si la scène chargée n'en contient pas — mais seulement là où il y a un
        /// oiseau à piloter, pour ne pas la faire apparaître par-dessus les menus.
        /// AfterSceneLoad, comme l'écouteur Kinect : une jauge posée à la main (avec son style)
        /// doit gagner sur celle-ci.
        ///
        /// L'abonnement à sceneLoaded n'est pas cosmétique : entre deux visiteurs, GameOverUI
        /// recharge la scène (SceneManager.LoadScene). Sans cela la jauge disparaîtrait dès la
        /// deuxième partie, ce qui est exactement le scénario de la JPO.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!AutoBootstrap) return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

        private static void TrySpawn()
        {
            if (!AutoBootstrap || Instance != null) return;
            if (Object.FindAnyObjectByType<MoveBird>() == null) return;

            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("GlideGauge (auto)", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            // Sous les autres panneaux : l'écran de fin de partie doit passer devant la jauge
            // pendant les quelques dixièmes de seconde où elle finit de s'effacer.
            go.transform.SetAsFirstSibling();
            go.AddComponent<GlideGauge>().targetCanvas = canvas;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null) targetCanvas = Object.FindAnyObjectByType<Canvas>();

            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            if (fillImage == null && percentText == null) BuildDefaultGauge();
            if (fillImage != null) fillRect = fillImage.rectTransform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (bird == null)
            {
                bird = Object.FindAnyObjectByType<MoveBird>();
                if (bird == null)
                {
                    Fade(0f);
                    return;
                }
            }

            // La partie est finie : l'écran de résultat prend le relais, la jauge s'efface.
            bool playing = !MoveBird.IsDead && !MoveBird.IsWon;
            Fade(playing ? 1f : 0f);
            if (!playing) return;

            // Lissage indépendant du framerate (le jeu tourne à 60 Hz, les tests bien plus haut).
            float target = Mathf.Clamp01(bird.GlideRatio);
            displayedRatio = Mathf.Lerp(displayedRatio, target,
                1f - Mathf.Exp(-smoothing * Time.deltaTime));

            ApplyRatio(displayedRatio);
        }

        private void ApplyRatio(float ratio)
        {
            Color color = ratio < 0.5f
                ? Color.Lerp(diveColor, midColor, ratio * 2f)
                : Color.Lerp(midColor, glideColor, (ratio - 0.5f) * 2f);

            if (fillImage != null)
            {
                fillImage.color = color;

                if (fillImage.type == Image.Type.Filled)
                {
                    fillImage.fillAmount = ratio;
                }
                else if (fillRect != null)
                {
                    // Sans sprite, un Image en mode Filled ne dessine rien de fiable : on étire
                    // directement le rect depuis le bas, ce qui donne le même résultat sans asset.
                    Vector2 max = fillRect.anchorMax;
                    max.y = ratio;
                    fillRect.anchorMax = max;
                }
            }

            if (percentText != null)
            {
                int percent = Mathf.RoundToInt(ratio * 100f);
                if (percent != displayedPercent)
                {
                    displayedPercent = percent;
                    percentText.text = PercentStrings[Mathf.Clamp(percent, 0, 100)];
                }
                percentText.color = color;
            }

            if (!pulseOnFullGlide || barRect == null) return;

            // Respiration légère quand la posture « bras tendus » est tenue : un retour positif
            // immédiat vaut mieux qu'une consigne écrite pour un visiteur qui découvre le jeu.
            if (ratio >= fullGlideThreshold)
            {
                pulseTime += Time.deltaTime;
                float pulse = 1f + Mathf.Sin(pulseTime * 6f) * 0.025f;
                barRect.localScale = new Vector3(pulse, 1f, 1f);
            }
            else
            {
                pulseTime = 0f;
                barRect.localScale = Vector3.one;
            }
        }

        private void Fade(float targetAlpha)
        {
            if (group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime / fadeDuration);
        }

        /// <summary>
        /// Construit la jauge par défaut : cadre bois, rail sombre, remplissage coloré, gros
        /// pourcentage et titre. Rien n'est sérialisé dans la scène, donc rien à fusionner quand
        /// plusieurs personnes travaillent sur Blocks.unity en même temps.
        /// </summary>
        private void BuildDefaultGauge()
        {
            var root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            if (targetCanvas != null && root.parent == null)
            {
                root.SetParent(targetCanvas.transform, false);
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = margin;
            root.sizeDelta = size;

            if (font == null) font = ResolveHudFont();

            // Pourcentage, tout en haut du bloc, puis le titre juste en dessous.
            percentText = CreateText("Percent", root, 52, TextAnchor.MiddleCenter);
            PlaceFromTop(percentText.rectTransform, 0f, 66f);
            percentText.text = PercentStrings[100];

            captionText = CreateText("Caption", root, 22, TextAnchor.MiddleCenter);
            PlaceFromTop(captionText.rectTransform, 66f, 30f);
            captionText.text = "PLANÉ";
            captionText.color = Cream;

            // Cadre bois occupant toute la hauteur restante.
            var frame = CreateImage("Frame", root, WoodDark);
            barRect = frame.rectTransform;
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = new Vector2(0f, -104f);
            if (frameSprite != null)
            {
                frame.sprite = frameSprite;
                // Simple plutôt que Sliced : le bois n'a pas de bordures 9-slice définies, et
                // Sliced ferait cracher un avertissement dans la console à chaque partie.
                frame.type = Image.Type.Simple;
                frame.color = Color.white;
            }

            // Rail sombre, en retrait du cadre.
            trackImage = CreateImage("Track", barRect, TrackDark);
            Stretch(trackImage.rectTransform, 9f);

            // Remplissage, ancré en bas : sa hauteur EST le pourcentage.
            fillImage = CreateImage("Fill", trackImage.rectTransform, glideColor);
            var fr = fillImage.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(1f, 1f);
            // Marges horizontales seulement : une marge basse ferait passer le rect en hauteur
            // négative à 0 %, et le remplissage se retournerait au lieu de disparaître.
            fr.offsetMin = new Vector2(3f, 0f);
            fr.offsetMax = new Vector2(-3f, 0f);
        }

        /// <summary>
        /// Reprend la police déjà utilisée par le HUD (le score), pour que la jauge ne détonne pas
        /// avec le reste des menus. Filet de sécurité sur la police intégrée d'Unity.
        /// </summary>
        private Font ResolveHudFont()
        {
            if (targetCanvas != null)
            {
                var texts = targetCanvas.GetComponentsInChildren<Text>(true);
                foreach (var t in texts)
                {
                    if (t != null && t.font != null) return t.font;
                }
            }

            // LegacyRuntime.ttf est la police intégrée depuis Unity 2022 (ex-Arial.ttf).
            Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return builtin != null ? builtin : Font.CreateDynamicFontFromOSFont("Arial", 32);
        }

        private Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Cream;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Contour sombre : indispensable sur un vidéoprojecteur, où le ciel clair du jeu
            // passe derrière le HUD et mange les lettres crème.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.05f, 0.02f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Bande horizontale pleine largeur, posée à `topOffset` pixels sous le haut du parent.</summary>
        private static void PlaceFromTop(RectTransform rect, float topOffset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -topOffset);
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
