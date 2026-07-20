#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using network;
using Scenes.main_UdonProgramSources;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LocalizationSetup
{
    private const string TablePath = "Assets/Localization/strings.tsv";
    private const string ScenePath = "Assets/Scenes/main.unity";
    private const string ManagerScriptPath = "Assets/Scripts/LocalizationManager.cs";
    private const string ManagerProgramPath =
        "Assets/Scenes/main_UdonProgramSources/LocalizationManager Udon C# Program Asset.asset";

    private sealed class Table
    {
        public readonly List<string> Keys = new List<string>();
        public readonly List<string> Chinese = new List<string>();
        public readonly List<string> English = new List<string>();
    }

    [MenuItem("Tools/re-Gravity/Setup Localization In Main Scene")]
    public static void SetupMainScene()
    {
        Table table = ReadAndValidateTable();
        EnsureManagerProgramAsset();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        CtrlPanel ctrlPanel = UnityEngine.Object.FindObjectOfType<CtrlPanel>(true);
        SyncManager syncManager = UnityEngine.Object.FindObjectOfType<SyncManager>(true);
        DocumentationController documentationController =
            UnityEngine.Object.FindObjectOfType<DocumentationController>(true);
        if (ctrlPanel == null || syncManager == null)
        {
            throw new InvalidOperationException("Could not find CtrlPanel and SyncManager in main scene.");
        }

        LocalizationManager manager = UnityEngine.Object.FindObjectOfType<LocalizationManager>(true);
        if (manager == null)
        {
            GameObject managerObject = new GameObject("LocalizationManager");
            manager = managerObject.AddUdonSharpComponent<LocalizationManager>();
        }

        HashSet<string> validKeys = new HashSet<string>(table.Keys, StringComparer.Ordinal);
        Dictionary<TextMeshProUGUI, string> existingKeyByTarget =
            new Dictionary<TextMeshProUGUI, string>();
        int oldTargetCount = manager.textTargets == null ? 0 : manager.textTargets.Length;
        int oldKeyCount = manager.textKeys == null ? 0 : manager.textKeys.Length;
        for (int i = 0; i < Math.Min(oldTargetCount, oldKeyCount); i++)
        {
            if (manager.textTargets[i] != null && validKeys.Contains(manager.textKeys[i]))
            {
                existingKeyByTarget[manager.textTargets[i]] = manager.textKeys[i];
            }
        }

        manager.keys = table.Keys.ToArray();
        manager.zhCNTexts = table.Chinese.ToArray();
        manager.englishTexts = table.English.ToArray();
        manager.ctrlPanel = ctrlPanel;
        manager.syncManager = syncManager;
        manager.documentationController = documentationController;

        Dictionary<string, string> keyByChineseText = new Dictionary<string, string>();
        for (int i = 0; i < table.Keys.Count; i++)
        {
            if (table.Keys[i].StartsWith("ui.", StringComparison.Ordinal))
            {
                keyByChineseText[table.Chinese[i]] = table.Keys[i];
            }
        }

        List<TextMeshProUGUI> targets = new List<TextMeshProUGUI>();
        List<string> targetKeys = new List<string>();
        TextMeshProUGUI[] allTexts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true)
            .OrderBy(GetHierarchyPath)
            .ToArray();
        foreach (TextMeshProUGUI text in allTexts)
        {
            string key;
            if (!existingKeyByTarget.TryGetValue(text, out key) &&
                !keyByChineseText.TryGetValue(text.text, out key)) continue;
            targets.Add(text);
            targetKeys.Add(key);
        }

        manager.textTargets = targets.ToArray();
        manager.textKeys = targetKeys.ToArray();
        manager.specialValueSliders = new[] { ctrlPanel.batchCountSlider };
        manager.specialValueSliderKeys = new[] { "slider.auto" };
        manager.specialValueDuoSliders = Array.Empty<TextDuoSlider>();
        manager.specialValueDuoSliderKeysA = Array.Empty<string>();
        manager.specialValueDuoSliderKeysB = Array.Empty<string>();

        manager.chineseButton = FindButton("zh-cn");
        manager.englishButton = FindButton("en-us");
        if (manager.chineseButton == null || manager.englishButton == null)
        {
            throw new InvalidOperationException("Could not find language buttons named zh-cn and en-us.");
        }

        ctrlPanel.localizationManager = manager;
        syncManager.localizationManager = manager;
        if (documentationController != null)
        {
            documentationController.localizationManager = manager;
        }

        VRC.Udon.UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
        ConfigureLanguageButton(manager.chineseButton, backing, nameof(LocalizationManager.SelectChinese));
        ConfigureLanguageButton(manager.englishButton, backing, nameof(LocalizationManager.SelectEnglish));

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(ctrlPanel);
        EditorUtility.SetDirty(syncManager);
        if (documentationController != null) EditorUtility.SetDirty(documentationController);
        EditorUtility.SetDirty(manager.chineseButton);
        EditorUtility.SetDirty(manager.englishButton);

        UdonSharpEditorUtility.CopyProxyToUdon(manager, ProxySerializationPolicy.All);
        UdonSharpEditorUtility.CopyProxyToUdon(ctrlPanel, ProxySerializationPolicy.All);
        UdonSharpEditorUtility.CopyProxyToUdon(syncManager, ProxySerializationPolicy.All);
        if (documentationController != null)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(documentationController, ProxySerializationPolicy.All);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"Localization setup complete: {table.Keys.Count} strings, {targets.Count} static text bindings.");
    }

    [MenuItem("Tools/re-Gravity/Validate Localization Table")]
    public static void ValidateLocalizationTable()
    {
        Table table = ReadAndValidateTable();
        Debug.Log($"Localization table is valid: {table.Keys.Count} entries.");
    }

    private static Table ReadAndValidateTable()
    {
        string fullPath = Path.GetFullPath(TablePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Localization table not found.", fullPath);

        string[] lines = File.ReadAllLines(fullPath);
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != "key\tzh-CN\ten")
        {
            throw new InvalidDataException("Localization table header must be: key<TAB>zh-CN<TAB>en");
        }

        Table table = new Table();
        HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

            string[] columns = line.Split('\t');
            if (columns.Length != 3)
            {
                throw new InvalidDataException($"{TablePath}:{lineIndex + 1} must contain exactly 3 tab-separated columns.");
            }

            string key = columns[0].Trim();
            string chinese = Unescape(columns[1]);
            string english = Unescape(columns[2]);
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(chinese) || string.IsNullOrEmpty(english))
            {
                throw new InvalidDataException($"{TablePath}:{lineIndex + 1} contains an empty key or translation.");
            }

            if (!seenKeys.Add(key))
            {
                throw new InvalidDataException($"{TablePath}:{lineIndex + 1} contains duplicate key '{key}'.");
            }

            if (GetPlaceholderSignature(chinese) != GetPlaceholderSignature(english))
            {
                throw new InvalidDataException($"{TablePath}:{lineIndex + 1} has mismatched format placeholders for '{key}'.");
            }

            table.Keys.Add(key);
            table.Chinese.Add(chinese);
            table.English.Add(english);
        }

        return table;
    }

    private static void EnsureManagerProgramAsset()
    {
        UdonSharpProgramAsset programAsset =
            AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ManagerProgramPath);
        if (programAsset != null) return;

        MonoScript sourceScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ManagerScriptPath);
        if (sourceScript == null || sourceScript.GetClass() != typeof(LocalizationManager))
        {
            throw new InvalidOperationException("LocalizationManager.cs is not imported as a valid MonoScript.");
        }

        programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        programAsset.sourceCsScript = sourceScript;
        AssetDatabase.CreateAsset(programAsset, ManagerProgramPath);
        AssetDatabase.ImportAsset(ManagerProgramPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();

        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\n", "\n").Replace("\\t", "\t");
    }

    private static string GetPlaceholderSignature(string value)
    {
        List<string> placeholders = new List<string>();
        for (int i = 0; i < value.Length - 2; i++)
        {
            if (value[i] != '{' || !char.IsDigit(value[i + 1])) continue;
            int end = value.IndexOf('}', i + 2);
            if (end < 0) continue;
            placeholders.Add(value.Substring(i, end - i + 1));
            i = end;
        }
        placeholders.Sort(StringComparer.Ordinal);
        return string.Join("|", placeholders);
    }

    private static Button FindButton(string objectName)
    {
        return UnityEngine.Object.FindObjectsOfType<Button>(true)
            .FirstOrDefault(button => button.gameObject.name == objectName);
    }

    private static void ConfigureLanguageButton(Button button, VRC.Udon.UdonBehaviour target, string eventName)
    {
        while (button.onClick.GetPersistentEventCount() > 0)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
        }
        UnityEventTools.AddStringPersistentListener(button.onClick, target.SendCustomEvent, eventName);
    }

    private static string GetHierarchyPath(Component component)
    {
        string path = component.gameObject.name;
        Transform parent = component.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
#endif
