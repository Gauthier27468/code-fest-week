using System.Collections.Generic;
using System.IO;
using KiBird.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Construit l'écran de démarrage de KiBird de façon procédurale, directement dans la scène de
// jeu (Blocks.unity) : le menu et le jeu partagent la même scène, le menu se contentant de se
// masquer une fois le décompte terminé (voir MenuController.StartGame()).
// Utilisable depuis le menu Editor "KiBird/Build Main Menu Scene" ou en batch mode via
// -executeMethod KiBirdMenuBuilder.Build
public static class KiBirdMenuBuilder
{
    private const string GameScenePath = "Assets/Scenes/Blocks.unity";
    private const string MenuRootName = "KiBirdMenu";
    private const string LogoPath = "Assets/Art/custom/logo-kibird.png";

    // OS dynamic fonts (Font.CreateDynamicFontFromOSFont) turned out unreliable in the editor :
    // "Showcard Gothic" was reported as installed but rendered no glyphs at all. A bundled font
    // file, imported like any other asset, is the robust option. Luckiest Guy is a free
    // (SIL Open Font License) Google Font with the same chunky, rounded poster-title feel.
    private const string UiFontPath = "Assets/Art/Fonts/LuckiestGuy-Regular.ttf";

    private static readonly Color BackgroundOverlayColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color MintColor = new Color(0.78f, 0.93f, 0.82f);
    private static readonly Color BadgeRingColor = new Color(0.07f, 0.14f, 0.25f);
    private static readonly Color BadgeFigureColor = Color.black;
    private static readonly Color AccentColor = new Color(1f, 0.85f, 0.15f);
    private static readonly Color TextColor = Color.white;
    private static readonly Color FlapMotionArmColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);

    private static Font uiFont;

    [MenuItem("KiBird/Build Main Menu Scene")]
    public static void Build()
    {
        uiFont = AssetDatabase.LoadAssetAtPath<Font>(UiFontPath);
        if (uiFont == null)
        {
            Debug.LogWarning("KiBird : police introuvable -> " + UiFontPath +
                              " , utilisation de la police par défaut.");
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        Sprite circleSprite = CreateCircleSprite();
        Sprite logoSprite = LoadCustomSprite(LogoPath);

        // On ouvre la scène de jeu existante (Bird, blocks, hoops, lumière...) au lieu d'en
        // créer une vide : le menu vient s'ajouter par-dessus, sans toucher au reste.
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        // Un Build() précédent peut avoir déjà ajouté le menu : on le retire d'abord pour que
        // ré-exécuter cet outil reste idempotent et ne duplique rien.
        GameObject previousMenuRoot = GameObject.Find(MenuRootName);
        if (previousMenuRoot != null)
        {
            Object.DestroyImmediate(previousMenuRoot);
        }

        var menuRoot = new GameObject(MenuRootName);

        // Pas de nouvelle caméra : la scène de jeu en a déjà une (embarquée dans le prefab
        // Bird), en créer une seconde provoquerait un conflit d'AudioListener / de rendu.
        // Idem pour l'EventSystem : la scène en a déjà un (venu d'un autre outil/scène fusionnée,
        // avec InputSystemUIInputModule) - Unity n'en tolère qu'un seul à la fois.
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            BuildEventSystem(menuRoot.transform);
        }

        RectTransform canvasRT = BuildCanvas(out Canvas canvas);
        canvasRT.SetParent(menuRoot.transform, false);
        // Pas d'image de fond opaque ici : le menu est superposé à la vraie scène 3D du jeu
        // (Blocks.unity, caméra du prefab Bird), qui doit rester visible derrière l'UI.
        CreateBackgroundOverlay(canvasRT);
        CreateLogo(canvasRT, logoSprite);

        // --- Score + classement (haut gauche) ---
        RectTransform scorePanel = CreateRect(canvasRT, "ScoreLeaderboardPanel",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(560f, 560f), new Vector2(60f, -40f));
        var scoreLayout = scorePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        scoreLayout.spacing = 8f;
        scoreLayout.childAlignment = TextAnchor.UpperLeft;
        scoreLayout.childControlWidth = true;
        scoreLayout.childControlHeight = false;
        scoreLayout.childForceExpandWidth = true;

        CreateText(scorePanel.transform, "ScoreTitle", "YOUR SCORE :", 38, FontStyle.Bold,
            TextAnchor.UpperLeft, MintColor, 48f);
        Text lastScoreText = CreateText(scorePanel.transform, "ScoreValue", "9999999!!!", 68, FontStyle.Bold,
            TextAnchor.UpperLeft, AccentColor, 84f);
        lastScoreText.gameObject.AddComponent<PulseEffect>();
        CreateText(scorePanel.transform, "LeaderboardTitle", "LEADER BOARD :", 34, FontStyle.Bold,
            TextAnchor.UpperLeft, MintColor, 46f);
        Text leaderboardText = CreateText(scorePanel.transform, "LeaderboardList",
            "1.  999999\n2.  9866\n3.  8765\n4.  6543\n5.  2345", 30, FontStyle.Normal,
            TextAnchor.UpperLeft, TextColor, 260f);

        // --- Badge circulaire central : silhouette du joueur ---
        // Centré au milieu de l'écran (comme le logo, aligné en x=0).
        Vector2 badgeCenter = Vector2.zero;
        RectTransform badgeRing = CreateRect(canvasRT, "PlayerBadgeRing",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(300f, 300f), badgeCenter);
        Image ringImg = badgeRing.gameObject.AddComponent<Image>();
        ringImg.sprite = circleSprite;
        ringImg.color = BadgeRingColor;
        ringImg.raycastTarget = false;

        RectTransform progressRT = CreateRect(badgeRing, "StartProgressFill",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(270f, 270f), Vector2.zero);
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
            new Vector2(230f, 230f), Vector2.zero);
        Image innerImg = badgeInner.gameObject.AddComponent<Image>();
        innerImg.sprite = circleSprite;
        innerImg.color = Color.white;
        innerImg.raycastTarget = false;

        RectTransform silhouetteSlot = CreateRect(badgeInner, "SilhouetteSlot",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(190f, 190f), Vector2.zero);
        var (silhouetteRoot, silBody, silLeftArm, silRightArm) =
            CreateSilhouetteParts(silhouetteSlot, "PlayerSilhouette", 190f, BadgeFigureColor, circleSprite);
        SilhouetteRig playerSilhouette = silhouetteRoot.AddComponent<SilhouetteRig>();
        playerSilhouette.Configure(silBody, silLeftArm, silRightArm);

        // --- Invite / décompte, sous le badge ---
        Text promptText = CreateText(canvasRT, "PromptText",
            "Tendez les bras pendant 3 secondes pour lancer le jeu.",
            40, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, 90f);
        RectTransform promptRT = promptText.GetComponent<RectTransform>();
        promptRT.anchorMin = new Vector2(0.5f, 0.5f);
        promptRT.anchorMax = new Vector2(0.5f, 0.5f);
        promptRT.pivot = new Vector2(0.5f, 0.5f);
        promptRT.sizeDelta = new Vector2(560f, 90f);
        promptRT.anchoredPosition = badgeCenter + new Vector2(0f, -200f);
        Outline promptOutline = promptText.gameObject.AddComponent<Outline>();
        promptOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        promptOutline.effectDistance = new Vector2(2f, -2f);

        // --- Silhouettes de tutoriel disposées en arc autour du badge ---
        // Laissées désactivées par défaut (masquées manuellement) : on les garde dans la
        // hiérarchie, prêtes à être réactivées depuis l'Inspector si besoin, sans avoir à
        // relancer le builder.
        GameObject dontFallItem = CreateRadialPoseItem(canvasRT, circleSprite, BirdPose.Glide,
            new Vector2(640f, 300f), 190f, "DON'T FALL!", new Vector2(120f, -140f), -8f);

        GameObject flyUpItem = CreateRadialPoseItem(canvasRT, circleSprite, BirdPose.FlapUp,
            new Vector2(680f, -20f), 190f, "FLY UP!", new Vector2(90f, -130f), -8f,
            addFlapMotionArms: true);

        GameObject turnItem = CreateRadialPoseItem(canvasRT, circleSprite, BirdPose.TiltLeft,
            new Vector2(560f, -320f), 170f, "TURN!", new Vector2(150f, -60f), -8f);

        GameObject speedUpItem = CreateSpeedUpItem(canvasRT, circleSprite, new Vector2(-380f, -340f));

        dontFallItem.SetActive(false);
        flyUpItem.SetActive(false);
        turnItem.SetActive(false);
        speedUpItem.SetActive(false);

        // --- Repère clavier de démo (à retirer une fois la Kinect branchée) ---
        // Masqué par défaut lui aussi.
        Text debugHint = CreateText(canvasRT, "DebugKeyboardHint",
            "[Mode test clavier, en attendant la Kinect]  Espace = bras horizontaux   ←/→ = inclinaison   ↑ = battement",
            22, FontStyle.Italic, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.6f), 40f);
        RectTransform debugRT = debugHint.GetComponent<RectTransform>();
        debugRT.anchorMin = new Vector2(0f, 0f);
        debugRT.anchorMax = new Vector2(1f, 0f);
        debugRT.pivot = new Vector2(0.5f, 0f);
        debugRT.anchoredPosition = new Vector2(0f, 16f);
        debugRT.sizeDelta = new Vector2(0f, 40f);
        debugHint.gameObject.SetActive(false);

        // --- Objet de logique : entrée démo + contrôleur du menu ---
        var controllerGO = new GameObject("MenuController");
        controllerGO.transform.SetParent(menuRoot.transform, false);
        DemoKeyboardInput input = controllerGO.AddComponent<DemoKeyboardInput>();
        KinectStartInputSource kinectInput = controllerGO.AddComponent<KinectStartInputSource>();
        MenuController controller = controllerGO.AddComponent<MenuController>();
        controller.Configure(input, kinectInput, playerSilhouette, lastScoreText, leaderboardText, promptText,
            progressFill, menuRoot);

        RegisterSceneInBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log("KiBird : menu ajouté à la scène de jeu -> " + GameScenePath);
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

    // Léger voile semi-transparent (pas opaque) pour que le texte/l'UI reste lisible
    // par-dessus la scène 3D du jeu, qui doit rester visible derrière.
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
            new Vector2(0.5f, 1f), new Vector2(680f, 340f), new Vector2(0f, -10f));
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
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

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

    // Regroupe figure + étiquette sous un même conteneur (retourné) afin qu'on puisse les
    // masquer ensemble d'un seul SetActive(false).
    private static GameObject CreateRadialPoseItem(Transform parent, Sprite circleSprite, BirdPose pose,
        Vector2 figurePos, float figureSize, string label, Vector2 labelOffset, float labelRotation,
        bool addFlapMotionArms = false)
    {
        RectTransform container = CreateRect(parent, "TutorialItem_" + label, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform figureSlot = CreateRect(container, "PoseFigure_" + label, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(figureSize, figureSize), figurePos);
        var (root, body, leftArm, rightArm) =
            CreateSilhouetteParts(figureSlot, "Figure", figureSize, MintColor, circleSprite);
        SilhouetteRig.ApplyStaticPose(body, leftArm, rightArm, pose);

        if (addFlapMotionArms)
        {
            CreateFlapMotionArms(root.transform, figureSize, FlapMotionArmColor);
        }

        CreateRotatedLabel(container, "Label_" + label, label, figurePos + labelOffset, labelRotation);

        return container.gameObject;
    }

    // Deuxième paire de bras grise, translucide et pointant vers le bas : superposée aux bras
    // normaux, elle suggère le battement (position basse pendant que la pose figée montre la
    // position haute), sans avoir besoin d'animation.
    private static void CreateFlapMotionArms(Transform root, float height, Color color)
    {
        float torsoHeight = height * 0.5f;
        float torsoWidth = height * 0.2f;
        float headSize = height * 0.26f;
        float armLength = height * 0.46f;
        float armThickness = height * 0.1f;
        float torsoY = -headSize * 0.3f;
        float shoulderY = torsoY + torsoHeight * 0.36f;
        const float armAngleDown = 70f;

        RectTransform leftArm = CreateSilhouetteImage(root, "FlapArmLeft", null, color,
            new Vector2(1f, 0.5f), new Vector2(armLength, armThickness), new Vector2(-torsoWidth / 2f, shoulderY));
        leftArm.localEulerAngles = new Vector3(0f, 0f, armAngleDown);

        RectTransform rightArm = CreateSilhouetteImage(root, "FlapArmRight", null, color,
            new Vector2(0f, 0.5f), new Vector2(armLength, armThickness), new Vector2(torsoWidth / 2f, shoulderY));
        rightArm.localEulerAngles = new Vector3(0f, 0f, -armAngleDown);
    }

    private static void CreateRotatedLabel(Transform parent, string name, string label, Vector2 position,
        float rotation)
    {
        Text labelText = CreateText(parent, name, label, 34, FontStyle.Bold, TextAnchor.MiddleCenter, AccentColor,
            60f);
        labelText.gameObject.AddComponent<PulseEffect>();
        RectTransform labelRT = labelText.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0.5f, 0.5f);
        labelRT.anchorMax = new Vector2(0.5f, 0.5f);
        labelRT.pivot = new Vector2(0.5f, 0.5f);
        labelRT.sizeDelta = new Vector2(300f, 60f);
        labelRT.anchoredPosition = position;
        labelRT.localEulerAngles = new Vector3(0f, 0f, rotation);

        Outline outline = labelText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private static void CreateArrowGlyph(Transform parent, string name, string glyph, Vector2 position,
        float rotation)
    {
        Text arrowText = CreateText(parent, name, glyph, 44, FontStyle.Bold, TextAnchor.MiddleCenter, AccentColor,
            50f);
        RectTransform rt = arrowText.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(60f, 60f);
        rt.anchoredPosition = position;
        rt.localEulerAngles = new Vector3(0f, 0f, rotation);
    }

    private static GameObject CreateSpeedUpItem(Transform parent, Sprite circleSprite, Vector2 centerPos)
    {
        RectTransform container = CreateRect(parent, "TutorialItem_SpeedUp", new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        RectTransform mainSlot = CreateRect(container, "SpeedFigureMain", new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(180f, 180f), centerPos);
        var (_, mainBody, mainLeftArm, mainRightArm) =
            CreateSilhouetteParts(mainSlot, "Figure", 180f, MintColor, circleSprite);
        SilhouetteRig.ApplyStaticPose(mainBody, mainLeftArm, mainRightArm, BirdPose.Neutral);

        RectTransform companionSlot = CreateRect(container, "SpeedFigureCompanion", new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110f, 110f),
            centerPos + new Vector2(95f, -10f));
        var (_, compBody, compLeftArm, compRightArm) =
            CreateSilhouetteParts(companionSlot, "Figure", 110f, Color.white, circleSprite);
        SilhouetteRig.ApplyStaticPose(compBody, compLeftArm, compRightArm, BirdPose.Neutral);

        CreateArrowGlyph(container, "SpeedArrow", "↙", centerPos + new Vector2(15f, -110f), -15f);

        CreateRotatedLabel(container, "Label_SpeedUp", "SPEED UP!!!", centerPos + new Vector2(150f, -110f), -8f);

        return container.gameObject;
    }
}
