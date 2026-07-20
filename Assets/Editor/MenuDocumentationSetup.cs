#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;

public static class MenuDocumentationSetup
{
    private const string ScenePath = "Assets/Scenes/main.unity";
    private const string ControllerScriptPath = "Assets/Scripts/DocumentationController.cs";
    private const string ControllerProgramPath =
        "Assets/Scenes/main_UdonProgramSources/DocumentationController Udon C# Program Asset.asset";
    private const string TabPrefabPath = "Assets/UI/TabBtn.prefab";
    private const string PageButtonPrefabPath = "Assets/UI/PageBtn.prefab";
    private const string SmileyFontPath = "Assets/UI/Fonts/SmileySans SDF.asset";
    private const string SmileyMaterialPath = "Assets/UI/Theme/SmileySans Primary.mat";
    private const string UiMaterialPath = "Assets/UI/Theme/UI Primary.mat";
    private const string OutlineSpritePath = "Assets/UI/Sprites/outline.png";
    private const string BodyStructurePath = "Assets/UI/Sprites/doc body structure.png";
    private const string GravityFormulaPath = "Assets/UI/Sprites/doc gravity formula.png";

    private static TMP_FontAsset _smileyFont;
    private static Material _smileyMaterial;
    private static Material _uiMaterial;
    private static Sprite _outlineSprite;
    private static GameObject _pageButtonPrefab;

    [MenuItem("Tools/re-Gravity/Setup About Tab And Documentation")]
    public static void SetupMainScene()
    {
        EnsureControllerProgramAsset();
        LoadUiAssets();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform pageContainer = UnityEngine.Object.FindObjectsOfType<RectTransform>(true)
            .FirstOrDefault(item => item.gameObject.name == "Page_Container");
        if (pageContainer == null)
        {
            throw new InvalidOperationException("Could not find Page_Container.");
        }

        Transform mainMenuRoot = pageContainer.parent;
        Canvas canvas = mainMenuRoot == null ? null : mainMenuRoot.GetComponentInParent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("Could not find the main menu Canvas.");
        Transform panelRoot = canvas.transform.parent;
        if (panelRoot == null) throw new InvalidOperationException("Could not find PanelRoot.");

        Transform sidebar = mainMenuRoot.Find("Menu_Sidebar");
        if (sidebar == null || pageContainer == null)
        {
            throw new InvalidOperationException("Could not find the sidebar or page container.");
        }

        DestroyChild(sidebar, "TabBtnAbout");
        DestroyChild(pageContainer, "Page_About");
        DestroyChild(canvas.transform, "DocumentationRoot");
        DestroyChild(canvas.transform, "DocumentationController");
        DestroyChild(panelRoot, "DocumentationCanvas");
        DestroyChild(panelRoot, "DocumentationController");

        GameObject aboutTab = CreateAboutTab(sidebar);
        GameObject aboutPage = CreateAboutPage(pageContainer, out Button openDocumentButton);
        GameObject documentationCanvas = CreateDocumentationCanvas(canvas, panelRoot);
        GameObject documentRoot = CreateDocumentRoot(documentationCanvas.transform,
            out TextMeshProUGUI titleText,
            out TextMeshProUGUI bodyText, out TextMeshProUGUI indicatorText, out Image pageImage,
            out Button previousButton, out Button nextButton, out Button closeButton);

        GameObject controllerObject = new GameObject("DocumentationController");
        controllerObject.transform.SetParent(panelRoot, false);
        DocumentationController controller = controllerObject.AddUdonSharpComponent<DocumentationController>();
        controller.mainMenuRoot = canvas.gameObject;
        controller.documentRoot = documentationCanvas;
        controller.previousButton = previousButton;
        controller.nextButton = nextButton;
        controller.titleText = titleText;
        controller.bodyText = bodyText;
        controller.pageIndicatorText = indicatorText;
        controller.pageImage = pageImage;
        controller.pageTitleKeys = Enumerable.Range(1, 11)
            .Select(index => $"doc.{index:00}.title")
            .ToArray();
        controller.pageBodyKeys = Enumerable.Range(1, 11)
            .Select(index => $"doc.{index:00}.body")
            .ToArray();

        Sprite bodyStructure = LoadDocumentationSprite(BodyStructurePath);
        Sprite gravityFormula = LoadDocumentationSprite(GravityFormulaPath);
        controller.pageImages = new Sprite[]
        {
            null, null, null, bodyStructure, gravityFormula, null, null, null, null, null, null
        };

        VRC.Udon.UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
        ConfigureUdonButton(openDocumentButton, backing, nameof(DocumentationController.OpenDocument));
        ConfigureUdonButton(previousButton, backing, nameof(DocumentationController.PreviousPage));
        ConfigureUdonButton(nextButton, backing, nameof(DocumentationController.NextPage));
        ConfigureUdonButton(closeButton, backing, nameof(DocumentationController.CloseDocument));

        ConfigureTabs(sidebar, pageContainer);
        aboutPage.SetActive(false);
        documentationCanvas.SetActive(false);

        BoxCollider canvasCollider = canvas.GetComponent<BoxCollider>();
        if (canvasCollider != null)
        {
            canvasCollider.size = new Vector3(458f, 420f, 1f);
            canvasCollider.isTrigger = true;
        }

        PanelHandler panelHandler = UnityEngine.Object.FindObjectOfType<PanelHandler>(true);
        if (panelHandler == null) throw new InvalidOperationException("Could not find PanelHandler.");
        panelHandler.alternateCanvas = documentationCanvas;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(panelHandler);
        EditorUtility.SetDirty(aboutTab);
        EditorUtility.SetDirty(aboutPage);
        EditorUtility.SetDirty(documentationCanvas);
        EditorUtility.SetDirty(documentRoot);
        if (canvasCollider != null) EditorUtility.SetDirty(canvasCollider);
        UdonSharpEditorUtility.CopyProxyToUdon(controller, ProxySerializationPolicy.All);
        UdonSharpEditorUtility.CopyProxyToUdon(panelHandler, ProxySerializationPolicy.All);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        LocalizationSetup.SetupMainScene();
        Debug.Log("About tab and 11-page documentation setup complete.");
    }

    private static GameObject CreateDocumentationCanvas(Canvas mainCanvas, Transform parent)
    {
        GameObject gameObject = new GameObject("DocumentationCanvas", typeof(RectTransform));
        gameObject.layer = mainCanvas.gameObject.layer;
        gameObject.transform.SetParent(parent, false);

        RectTransform mainRect = mainCanvas.GetComponent<RectTransform>();
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.localPosition = mainRect.localPosition;
        rect.localRotation = mainRect.localRotation;
        rect.localScale = mainRect.localScale;
        rect.anchorMin = mainRect.anchorMin;
        rect.anchorMax = mainRect.anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = mainRect.anchoredPosition;
        rect.sizeDelta = new Vector2(760f, 500f);

        Canvas documentCanvas = gameObject.AddComponent<Canvas>();
        documentCanvas.renderMode = RenderMode.WorldSpace;
        documentCanvas.pixelPerfect = mainCanvas.pixelPerfect;
        documentCanvas.additionalShaderChannels = mainCanvas.additionalShaderChannels;
        documentCanvas.sortingLayerID = mainCanvas.sortingLayerID;
        documentCanvas.sortingOrder = mainCanvas.sortingOrder;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        CanvasScaler mainScaler = mainCanvas.GetComponent<CanvasScaler>();
        if (mainScaler != null)
        {
            scaler.uiScaleMode = mainScaler.uiScaleMode;
            scaler.scaleFactor = mainScaler.scaleFactor;
            scaler.referencePixelsPerUnit = mainScaler.referencePixelsPerUnit;
            scaler.dynamicPixelsPerUnit = mainScaler.dynamicPixelsPerUnit;
        }

        GraphicRaycaster raycaster = gameObject.AddComponent<GraphicRaycaster>();
        GraphicRaycaster mainRaycaster = mainCanvas.GetComponent<GraphicRaycaster>();
        if (mainRaycaster != null)
        {
            raycaster.ignoreReversedGraphics = mainRaycaster.ignoreReversedGraphics;
            raycaster.blockingObjects = mainRaycaster.blockingObjects;
            raycaster.blockingMask = mainRaycaster.blockingMask;
        }

        gameObject.AddComponent<VRCUiShape>();
        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(760f, 500f, 1f);
        collider.center = Vector3.zero;
        return gameObject;
    }

    private static void EnsureControllerProgramAsset()
    {
        UdonSharpProgramAsset programAsset =
            AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ControllerProgramPath);
        if (programAsset != null) return;

        MonoScript sourceScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ControllerScriptPath);
        if (sourceScript == null || sourceScript.GetClass() != typeof(DocumentationController))
        {
            throw new InvalidOperationException("DocumentationController.cs is not imported as a valid MonoScript.");
        }

        programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        programAsset.sourceCsScript = sourceScript;
        AssetDatabase.CreateAsset(programAsset, ControllerProgramPath);
        AssetDatabase.ImportAsset(ControllerProgramPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();
        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
    }

    private static void LoadUiAssets()
    {
        _smileyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SmileyFontPath);
        _smileyMaterial = AssetDatabase.LoadAssetAtPath<Material>(SmileyMaterialPath);
        _uiMaterial = AssetDatabase.LoadAssetAtPath<Material>(UiMaterialPath);
        _outlineSprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutlineSpritePath);
        _pageButtonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PageButtonPrefabPath);

        if (_smileyFont == null || _smileyMaterial == null || _uiMaterial == null ||
            _outlineSprite == null || _pageButtonPrefab == null)
        {
            throw new InvalidOperationException("One or more UI theme assets could not be loaded.");
        }
    }

    private static Sprite LoadDocumentationSprite(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Could not import documentation image at {path}.");
        }

        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
            importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidOperationException($"Could not load documentation sprite at {path}.");
        }
        return sprite;
    }

    private static GameObject CreateAboutTab(Transform sidebar)
    {
        GameObject tabPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TabPrefabPath);
        if (tabPrefab == null) throw new InvalidOperationException("Could not load TabBtn.prefab.");

        GameObject tab = (GameObject)PrefabUtility.InstantiatePrefab(tabPrefab, sidebar);
        tab.name = "TabBtnAbout";
        tab.transform.SetAsLastSibling();
        TextMeshProUGUI icon = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (icon != null) icon.text = "\uf02d";
        return tab;
    }

    private static GameObject CreateAboutPage(Transform parent, out Button openDocumentButton)
    {
        GameObject page = CreateRectObject("Page_About", parent);
        SetRect(page.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 370f));

        TextMeshProUGUI title = CreateText("Title", page.transform, "关于与文档", 26f,
            TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 156f), new Vector2(320f, 42f));

        string content =
            "<b>开源地址</b>\nhttps://github.com/donywang922/re-Gravity\n\n" +
            "<b>Bug 汇报</b>\n请通过 GitHub Issues 提交，并附上复现步骤、平台、显卡及截图。\n" +
            "https://github.com/donywang922/re-Gravity/issues\n\n" +
            "<b>使用的资产</b>\nVRChat Worlds SDK · UdonSharp · QvPen\n" +
            "TextMesh Pro · Font Awesome 6 Free · Smiley Sans";
        TextMeshProUGUI info = CreateText("Content", page.transform, content, 16f,
            TextAlignmentOptions.TopLeft);
        info.enableAutoSizing = true;
        info.fontSizeMin = 12f;
        info.fontSizeMax = 16f;
        SetRect(info.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 10f), new Vector2(320f, 238f));

        openDocumentButton = CreateButton("OpenDocument", page.transform, "打开完整文档", false);
        SetRect(openDocumentButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, -153f), new Vector2(280f, 48f));
        return page;
    }

    private static GameObject CreateDocumentRoot(Transform parent, out TextMeshProUGUI titleText,
        out TextMeshProUGUI bodyText, out TextMeshProUGUI indicatorText, out Image pageImage,
        out Button previousButton, out Button nextButton, out Button closeButton)
    {
        GameObject root = CreateRectObject("DocumentationRoot", parent);
        SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 500f));

        Image background = root.AddComponent<Image>();
        background.material = _uiMaterial;
        background.sprite = _outlineSprite;
        background.type = Image.Type.Sliced;
        background.color = Color.white;

        titleText = CreateText("PageTitle", root.transform, "", 28f, TextAlignmentOptions.Center);
        titleText.fontStyle = FontStyles.Bold;
        SetRect(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 218f), new Vector2(650f, 48f));

        closeButton = CreateButton("Close", root.transform, "\uf00d", true);
        SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(342f, 218f), new Vector2(44f, 40f));

        GameObject content = CreateRectObject("ContentLayout", root.transform);
        SetRect(content.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(700f, 365f));
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject imageObject = CreateRectObject("PageImage", content.transform);
        pageImage = imageObject.AddComponent<Image>();
        pageImage.preserveAspect = true;
        pageImage.raycastTarget = false;
        LayoutElement imageLayout = imageObject.AddComponent<LayoutElement>();
        imageLayout.preferredHeight = 145f;
        imageLayout.flexibleHeight = 0f;

        bodyText = CreateText("PageBody", content.transform, "", 17f, TextAlignmentOptions.TopLeft);
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 11f;
        bodyText.fontSizeMax = 16f;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        LayoutElement bodyLayout = bodyText.gameObject.AddComponent<LayoutElement>();
        bodyLayout.minHeight = 0f;
        bodyLayout.flexibleHeight = 1f;

        previousButton = CreateButton("PreviousPage", root.transform, "\uf053", true);
        SetRect(previousButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-94f, -220f), new Vector2(56f, 42f));

        nextButton = CreateButton("NextPage", root.transform, "\uf054", true);
        SetRect(nextButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(94f, -220f), new Vector2(56f, 42f));

        indicatorText = CreateText("PageIndicator", root.transform, "1 / 11", 17f,
            TextAlignmentOptions.Center);
        SetRect(indicatorText.rectTransform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(100f, 42f));

        return root;
    }

    private static GameObject CreateRectObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value,
        float fontSize, TextAlignmentOptions alignment)
    {
        GameObject gameObject = CreateRectObject(name, parent);
        TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _smileyFont;
        text.fontSharedMaterial = _smileyMaterial;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.richText = true;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, string label, bool useIconFont)
    {
        GameObject gameObject = (GameObject)PrefabUtility.InstantiatePrefab(_pageButtonPrefab, parent);
        gameObject.name = name;
        TextMeshProUGUI text = gameObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            if (!useIconFont)
            {
                text.font = _smileyFont;
                text.fontSharedMaterial = _smileyMaterial;
            }
            text.fontSize = useIconFont ? 20f : 18f;
            text.text = label;
        }
        return gameObject.GetComponent<Button>();
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void ConfigureTabs(Transform sidebar, Transform pageContainer)
    {
        string[] pageNames =
        {
            "Page_Start", "Page_Launch", "Page_Color", "Page_Setting",
            "Page_Player", "Page_Debug", "Page_About"
        };
        Dictionary<string, string> targetPageByButton = new Dictionary<string, string>
        {
            { "TabBtnStart", "Page_Start" },
            { "TabBtnLaunch", "Page_Launch" },
            { "TabBtnColor", "Page_Color" },
            { "TabBtnSetting", "Page_Setting" },
            { "TabBtnPlayer", "Page_Player" },
            { "TabBtnDebug", "Page_Debug" },
            { "TabBtnAbout", "Page_About" }
        };

        List<GameObject> pages = pageNames
            .Select(pageName => pageContainer.Find(pageName))
            .Where(page => page != null)
            .Select(page => page.gameObject)
            .ToList();
        if (pages.Count != pageNames.Length)
        {
            throw new InvalidOperationException("One or more menu pages could not be found.");
        }

        foreach (KeyValuePair<string, string> pair in targetPageByButton)
        {
            Transform buttonTransform = sidebar.Find(pair.Key);
            if (buttonTransform == null)
            {
                throw new InvalidOperationException($"Could not find tab button {pair.Key}.");
            }

            Button button = buttonTransform.GetComponent<Button>();
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            foreach (GameObject page in pages)
            {
                UnityAction<bool> setActive = page.SetActive;
                UnityEventTools.AddBoolPersistentListener(button.onClick, setActive,
                    page.name == pair.Value);
            }
            EditorUtility.SetDirty(button);
        }
    }

    private static void ConfigureUdonButton(Button button, VRC.Udon.UdonBehaviour target,
        string eventName)
    {
        while (button.onClick.GetPersistentEventCount() > 0)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
        }
        UnityEventTools.AddStringPersistentListener(button.onClick, target.SendCustomEvent, eventName);
        EditorUtility.SetDirty(button);
    }

    private static void DestroyChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }
}
#endif
