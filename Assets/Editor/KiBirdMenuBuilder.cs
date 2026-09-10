using System.Collections.Generic;
using System.IO;
using KiBird.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Reconstruit l'écran de démarrage de KiBird dans Blocks.unity.
// ATTENTION : détruit et régénère tout le contenu de Canvas/MainMenu. Les retouches faites à la
// main dans la scène sont perdues. Ne lancer que pour repartir d'un menu propre.
public static class KiBirdMenuBuilder
{
    private const string GameScenePath = "Assets/Scenes/Blocks.unity";
    private const string MenuRootName = "KiBirdMenu";
    private const string LogoPath = "Assets/Art/custom/logo-kibird.png";
    private const string UiFontPath = "Assets/Art/Fonts/LuckiestGuy-Regular.ttf";

    private static readonly Color BackgroundOverlayColor = new Color(0.02f, 0.05f, 0.10f, 0.25f);
    private static readonly Color CardBgColor = new Color(0.06f, 0.11f, 0.20f, 0.88f);
    private static readonly Color CardOutlineColor = new Color(0.35f, 0.65f, 0.95f, 0.35f);
    private static readonly Color BadgeRingColor = new Color(0.08f, 0.15f, 0.26f, 0.90f);
    private static readonly Color BadgeInnerColor = new Color(0.12f, 0.21f, 0.34f, 0.95f);
    private static readonly Color BadgeFigureColor = new Color(0.96f, 0.97f, 0.99f);
    private static readonly Color AccentColor = new Color(1f, 0.82f, 0.15f);
    private static readonly Color TextMutedColor = new Color(0.70f, 0.80f, 0.90f);
    private static readonly Color TextHeaderColor = new Color(0.40f, 0.90f, 0.95f);
    private static readonly Color TextColor = Color.white;

    private static Font uiFont;

    [MenuItem("KiBird/Build Main Menu Scene")]
    public static void Build()
    {
        uiFont = AssetDatabase.LoadAssetAtPath<Font>(UiFontPath);
        if (uiFont == null)
        {
            Debug.LogWarning("KiBird : police introuvable -> " + UiFontPath + " , utilisation de la police par défaut.");
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        Sprite circleSprite = CreateCircleSprite();
        Sprite roundedRectSprite = CreateRoundedRectSprite();
        Sprite logoSprite = LoadCustomSprite(LogoPath);

        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        GameObject menuRoot = GameObject.Find(MenuRootName);
        if (menuRoot == null)
        {
            menuRoot = new GameObject(MenuRootName);
        }

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            BuildEventSystem(menuRoot.transform);
        }

        Canvas canvas = menuRoot.GetComponentInChildren<Canvas>();
        RectTransform canvasRT;
        if (canvas == null)
        {
            canvasRT = BuildCanvas(out canvas);
            canvasRT.SetParent(menuRoot.transform, false);
        }
        else
        {
            canvasRT = canvas.GetComponent<RectTransform>();
        }

        GameObject gameMenuGO = null;
        Transform existingGameMenu = canvasRT.Find("GameMenu");
        if (existingGameMenu != null)
        {
            gameMenuGO = existingGameMenu.gameObject;
        }

        Transform existingMainMenu = canvasRT.Find("MainMenu");
        GameObject mainMenuRoot;
        if (existingMainMenu != null)
        {
            for (int i = existingMainMenu.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(existingMainMenu.GetChild(i).gameObject);
            }
            mainMenuRoot = existingMainMenu.gameObject;
        }
        else
        {
            mainMenuRoot = new GameObject("MainMenu", typeof(RectTransform));
            mainMenuRoot.transform.SetParent(canvasRT, false);
            RectTransform mmRT = mainMenuRoot.GetComponent<RectTransform>();
            mmRT.anchorMin = Vector2.zero;
            mmRT.anchorMax = Vector2.one;
            mmRT.sizeDelta = Vector2.zero;
            mmRT.anchoredPosition = Vector2.zero;
        }

        RectTransform mmTransform = mainMenuRoot.GetComponent<RectTransform>();

        CreateBackgroundOverlay(mmTransform);

        CreateLogo(mmTransform, logoSprite);

        RectTransform scoreCard = CreateRect(mmTransform, "ScoreCard",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(300f, 400f), new Vector2(48f, -40f));
        Image cardImg = scoreCard.gameObject.AddComponent<Image>();
        cardImg.sprite = roundedRectSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = CardBgColor;
        cardImg.raycastTarget = false;

        Outline cardOutline = scoreCard.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = CardOutlineColor;
        cardOutline.effectDistance = new Vector2(1.5f, -1.5f);

        var cardLayout = scoreCard.gameObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(20, 20, 16, 16);
        cardLayout.spacing = 6f;
        cardLayout.childAlignment = TextAnchor.UpperLeft;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = false;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        CreateText(scoreCard.transform, "Header", "🏆 SCORES", 22, FontStyle.Bold,
            TextAnchor.UpperLeft, TextHeaderColor, 28f);

        CreateDivider(scoreCard.transform, "Divider1", new Color(1f, 1f, 1f, 0.12f), 2f);

        RectTransform lastBlock = CreateRect(scoreCard.transform, "LastScoreBlock",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var lastLayout = lastBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        lastLayout.spacing = 2f;
        lastLayout.childControlWidth = true;
        lastLayout.childControlHeight = false;
        var lastLE = lastBlock.gameObject.AddComponent<LayoutElement>();
        lastLE.preferredHeight = 58f;

        CreateText(lastBlock.transform, "Title", "LAST SCORE", 15, FontStyle.Bold,
            TextAnchor.UpperLeft, TextMutedColor, 18f);
        Text lastScoreText = CreateText(lastBlock.transform, "Value", "0", 32, FontStyle.Bold,
            TextAnchor.UpperLeft, TextColor, 38f);

        CreateDivider(scoreCard.transform, "Divider2", new Color(1f, 1f, 1f, 0.10f), 2f);

        RectTransform bestBlock = CreateRect(scoreCard.transform, "BestScoreBlock",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var bestLayout = bestBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        bestLayout.spacing = 2f;
        bestLayout.childControlWidth = true;
        bestLayout.childControlHeight = false;
        var bestLE = bestBlock.gameObject.AddComponent<LayoutElement>();
        bestLE.preferredHeight = 62f;

        CreateText(bestBlock.transform, "Title", "★ BEST SCORE", 15, FontStyle.Bold,
            TextAnchor.UpperLeft, new Color(1f, 0.88f, 0.35f), 18f);
        Text bestScoreText = CreateText(bestBlock.transform, "Value", "0", 36, FontStyle.Bold,
            TextAnchor.UpperLeft, AccentColor, 40f);
        bestScoreText.gameObject.AddComponent<PulseEffect>();

        CreateDivider(scoreCard.transform, "Divider3", new Color(1f, 1f, 1f, 0.10f), 2f);

        RectTransform topBlock = CreateRect(scoreCard.transform, "TopScoresBlock",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var topLayout = topBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        topLayout.spacing = 2f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = false;
        var topLE = topBlock.gameObject.AddComponent<LayoutElement>();
        topLE.preferredHeight = 150f;

        CreateText(topBlock.transform, "Title", "TOP 5", 15, FontStyle.Bold,
            TextAnchor.UpperLeft, TextMutedColor, 18f);
        Text leaderboardText = CreateText(topBlock.transform, "Value", "-", 22, FontStyle.Normal,
            TextAnchor.UpperLeft, TextColor, 128f);

        Vector2 badgeCenter = new Vector2(0f, 15f);
        RectTransform badgeRing = CreateRect(mmTransform, "PlayerBadgeRing",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(260f, 260f), badgeCenter);
        Image ringImg = badgeRing.gameObject.AddComponent<Image>();
        ringImg.sprite = circleSprite;
        ringImg.color = BadgeRingColor;
        ringImg.raycastTarget = false;

        Outline ringOutline = badgeRing.gameObject.AddComponent<Outline>();
        ringOutline.effectColor = CardOutlineColor;
        ringOutline.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform progressRT = CreateRect(badgeRing, "StartProgressFill",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(236f, 236f), Vector2.zero);
        Image progressFill = progressRT.gameObject.AddComponent<Image>();
        progressFill.sprite = circleSprite;
        progressFill.color = AccentColor;
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Radial360;
        progressFill.fillOrigin = (int)Image.Origin360.Top;
        progressFill.fillClockwise = true;
        progressFill.fillAmount = 0f;
        progressFill.raycastTarget = false;

        RectTransform badgeInner = CreateRect(badgeRing, "Inner",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(204f, 204f), Vector2.zero);
        Image innerImg = badgeInner.gameObject.AddComponent<Image>();
        innerImg.sprite = circleSprite;
        innerImg.color = BadgeInnerColor;
        innerImg.raycastTarget = false;

        RectTransform silhouetteSlot = CreateRect(badgeInner, "SilhouetteSlot",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(165f, 165f), Vector2.zero);
        var (silhouetteRoot, silBody, silLeftArm, silRightArm) =
            CreateSilhouetteParts(silhouetteSlot, "PlayerSilhouette", 165f, BadgeFigureColor, circleSprite);
        SilhouetteRig playerSilhouette = silhouetteRoot.AddComponent<SilhouetteRig>();
        playerSilhouette.Configure(silBody, silLeftArm, silRightArm);

        RectTransform promptBanner = CreateRect(mmTransform, "PromptBanner",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(660f, 62f), new Vector2(0f, -185f));
        Image bannerImg = promptBanner.gameObject.AddComponent<Image>();
        bannerImg.sprite = roundedRectSprite;
        bannerImg.type = Image.Type.Sliced;
        bannerImg.color = CardBgColor;
        bannerImg.raycastTarget = false;

        Outline bannerOutline = promptBanner.gameObject.AddComponent<Outline>();
        bannerOutline.effectColor = CardOutlineColor;
        bannerOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Text promptText = CreateText(promptBanner.transform, "PromptText",
            "TENDEZ LES BRAS PENDANT 3 SECONDES POUR VOLER",
            22, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, 60f);
        RectTransform promptRT = promptText.GetComponent<RectTransform>();
        promptRT.anchorMin = Vector2.zero;
        promptRT.anchorMax = Vector2.one;
        promptRT.sizeDelta = Vector2.zero;
        promptRT.anchoredPosition = Vector2.zero;

        MenuController controller = menuRoot.GetComponentInChildren<MenuController>();
        DemoKeyboardInput input = menuRoot.GetComponentInChildren<DemoKeyboardInput>();
        KinectStartInputSource kinectInput = menuRoot.GetComponentInChildren<KinectStartInputSource>();

        if (controller == null)
        {
            var controllerGO = new GameObject("MenuController");
            controllerGO.transform.SetParent(menuRoot.transform, false);
            input = controllerGO.AddComponent<DemoKeyboardInput>();
            kinectInput = controllerGO.AddComponent<KinectStartInputSource>();
            controller = controllerGO.AddComponent<MenuController>();
        }

        controller.Configure(input, kinectInput, playerSilhouette, lastScoreText, bestScoreText,
            leaderboardText, promptText, progressFill, mainMenuRoot, gameMenuGO);

        RegisterSceneInBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log("KiBird : Menu principal reconstruit avec succès -> " + GameScenePath);
    }

    private static void RegisterSceneInBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == GameScenePath))
        {
            scenes.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    private static void BuildEventSystem(Transform parent)
    {
        var esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        esGO.transform.SetParent(parent, false);
    }

    private static RectTransform BuildCanvas(out Canvas canvas)
    {
        var canvasGO = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvasGO.GetComponent<RectTransform>();
    }

    private static void CreateBackgroundOverlay(Transform parent)
    {
        RectTransform overlay = CreateRect(parent, "BackgroundOverlay", Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image overlayImg = overlay.gameObject.AddComponent<Image>();
        overlayImg.color = BackgroundOverlayColor;
        overlayImg.raycastTarget = false;
    }

    private static void CreateLogo(Transform parent, Sprite logoSprite)
    {
        if (logoSprite == null) return;

        RectTransform logoRT = CreateRect(parent, "Logo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(620f, 310f), new Vector2(0f, -20f));
        Image img = logoRT.gameObject.AddComponent<Image>();
        img.sprite = logoSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static Sprite LoadCustomSprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            Debug.LogWarning("KiBird : image introuvable -> " + path);
            return null;
        }

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPosition;
        return rt;
    }

    private static Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style,
        TextAnchor alignment, Color color, float preferredHeight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var layoutElement = go.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleWidth = 1f;

        return text;
    }

    private static void CreateDivider(Transform parent, string name, Color color, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
    }

    private static Sprite CreateRoundedRectSprite()
    {
        const string dir = "Assets/Art/Generated";
        const string pngPath = dir + "/rounded_rect.png";

        if (!File.Exists(pngPath))
        {
            Directory.CreateDirectory(dir);
            const int size = 128;
            const int radius = 28;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                    int cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float alpha = Mathf.Clamp01(radius - d + 1f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        if (importer != null)
        {
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteBorder != new Vector4(28, 28, 28, 28))
            {
                importer.spriteBorder = new Vector4(28, 28, 28, 28);
                dirty = true;
            }
            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }

    private static Sprite CreateCircleSprite()
    {
        const string dir = "Assets/Art/Generated";
        const string pngPath = dir + "/circle.png";

        if (!File.Exists(pngPath))
        {
            Directory.CreateDirectory(dir);
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - d + 1f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        if (importer != null)
        {
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }

    private static (GameObject root, RectTransform body, RectTransform leftArm, RectTransform rightArm)
        CreateSilhouetteParts(Transform parent, string name, float height, Color color, Sprite headSprite)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta = new Vector2(height, height);
        rootRT.anchoredPosition = Vector2.zero;

        float torsoHeight = height * 0.5f;
        float torsoWidth = height * 0.2f;
        float headSize = height * 0.26f;
        float armLength = height * 0.46f;
        float armThickness = height * 0.1f;

        float torsoY = -headSize * 0.3f;
        float headY = torsoY + torsoHeight / 2f + headSize / 2f - height * 0.02f;
        float shoulderY = torsoY + torsoHeight * 0.36f;

        RectTransform torsoRT = CreateSilhouetteImage(rootRT, "Torso", null, color,
            new Vector2(0.5f, 0.5f), new Vector2(torsoWidth, torsoHeight), new Vector2(0f, torsoY));

        CreateSilhouetteImage(rootRT, "Head", headSprite, color,
            new Vector2(0.5f, 0.5f), new Vector2(headSize, headSize), new Vector2(0f, headY));

        RectTransform leftArmRT = CreateSilhouetteImage(rootRT, "LeftArm", null, color,
            new Vector2(1f, 0.5f), new Vector2(armLength, armThickness), new Vector2(-torsoWidth / 2f, shoulderY));

        RectTransform rightArmRT = CreateSilhouetteImage(rootRT, "RightArm", null, color,
            new Vector2(0f, 0.5f), new Vector2(armLength, armThickness), new Vector2(torsoWidth / 2f, shoulderY));

        return (root, torsoRT, leftArmRT, rightArmRT);
    }

    private static RectTransform CreateSilhouetteImage(Transform parent, string name, Sprite sprite, Color color,
        Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPosition;

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;

        return rt;
    }
}
