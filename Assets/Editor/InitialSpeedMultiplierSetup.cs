#if UNITY_EDITOR
using System;
using System.Linq;
using Scenes.main_UdonProgramSources;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class InitialSpeedMultiplierSetup
{
    private const string ScenePath = "Assets/Scenes/main.unity";
    private const string SliderPrefabPath = "Assets/UI/TextSlider.prefab";
    private const string SliderObjectName = "InitialSpeedMultiplier";

    [MenuItem("Tools/re-Gravity/Setup Initial Speed Multiplier")]
    public static void SetupMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CtrlPanel ctrlPanel = UnityEngine.Object.FindObjectOfType<CtrlPanel>(true);
        RectTransform settingsPage = UnityEngine.Object.FindObjectsOfType<RectTransform>(true)
            .FirstOrDefault(rect => rect.gameObject.name == "Page_Setting");
        if (ctrlPanel == null || settingsPage == null)
        {
            throw new InvalidOperationException("Could not find CtrlPanel or Page_Setting in main scene.");
        }

        Transform existing = settingsPage.Find(SliderObjectName);
        GameObject sliderObject;
        if (existing == null)
        {
            GameObject sliderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SliderPrefabPath);
            if (sliderPrefab == null)
            {
                throw new InvalidOperationException($"Could not load slider prefab at {SliderPrefabPath}.");
            }

            sliderObject = (GameObject)PrefabUtility.InstantiatePrefab(sliderPrefab, settingsPage);
            sliderObject.name = SliderObjectName;
        }
        else
        {
            sliderObject = existing.gameObject;
        }

        // Keep the reset note as the final row on the settings page.
        sliderObject.transform.SetSiblingIndex(Mathf.Max(0, settingsPage.childCount - 2));
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.0f, 1.0f);
        sliderRect.anchorMax = new Vector2(0.0f, 1.0f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.sizeDelta = new Vector2(350.0f, 36.0f);

        TextSlider textSlider = sliderObject.GetComponent<TextSlider>();
        if (textSlider == null || textSlider.slider == null)
        {
            throw new InvalidOperationException("Instantiated initial speed slider is missing TextSlider or Slider.");
        }

        textSlider.slider.minValue = 0.0f;
        textSlider.slider.maxValue = 2.0f;
        textSlider.slider.wholeNumbers = false;
        textSlider.step = 0.1f;
        textSlider.displayMultiplier = 1.0f;
        textSlider.decimalPlaces = 1;
        textSlider.useSpecialValue = false;
        textSlider.SetValueAndRefresh(1.0f);

        TextMeshProUGUI label = sliderObject.transform.Find("Text/Label")
            ?.GetComponent<TextMeshProUGUI>();
        if (label == null)
        {
            throw new InvalidOperationException("Could not find Text/Label on initial speed slider prefab.");
        }
        label.text = "初始速度倍率：";

        ctrlPanel.defInitialSpeedMultiplier = 1.0f;
        ctrlPanel.initialSpeedMultiplierSlider = textSlider;

        LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPage);
        EditorUtility.SetDirty(textSlider);
        EditorUtility.SetDirty(textSlider.slider);
        EditorUtility.SetDirty(label);
        EditorUtility.SetDirty(ctrlPanel);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sliderObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(textSlider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(textSlider.slider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(label);

        UdonSharpEditorUtility.CopyProxyToUdon(textSlider, ProxySerializationPolicy.All);
        UdonSharpEditorUtility.CopyProxyToUdon(ctrlPanel, ProxySerializationPolicy.All);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        // Re-import the table so the newly-created label participates in language switching.
        LocalizationSetup.SetupMainScene();
        Debug.Log("Initial speed multiplier setup complete: default 1.0, range 0-2.");
    }
}
#endif
