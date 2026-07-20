using network;
using Scenes.main_UdonProgramSources;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LocalizationManager : UdonSharpBehaviour
{
    private const int LanguageChinese = 0;
    private const int LanguageEnglish = 1;
    private const string LanguagePreferenceKey = "reGravity.locale.v1";

    [Header("Imported Language Table")]
    public string[] keys;
    public string[] zhCNTexts;
    public string[] englishTexts;

    [Header("Static Text Bindings")]
    public TextMeshProUGUI[] textTargets;
    public string[] textKeys;

    [Header("Special Slider Values")]
    public TextSlider[] specialValueSliders;
    public string[] specialValueSliderKeys;
    public TextDuoSlider[] specialValueDuoSliders;
    public string[] specialValueDuoSliderKeysA;
    public string[] specialValueDuoSliderKeysB;

    [Header("Language Controls")]
    public Button chineseButton;
    public Button englishButton;

    [Header("Dynamic Text Producers")]
    public CtrlPanel ctrlPanel;
    public SyncManager syncManager;
    public DocumentationController documentationController;

    private int _currentLanguage = -1;
    private int _pendingLanguage = -1;
    private bool _persistenceReady;
    private bool _hasManualOverride;

    public bool IsEnglish()
    {
        return _currentLanguage == LanguageEnglish;
    }

    private void Start()
    {
        if (_currentLanguage == -1)
        {
            ApplyLanguage(GetLanguageFromCode(VRCPlayerApi.GetCurrentLanguage()));
        }
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;

        _persistenceReady = true;

        if (_pendingLanguage != -1)
        {
            int pending = _pendingLanguage;
            _pendingLanguage = -1;
            _hasManualOverride = true;
            PlayerData.SetString(LanguagePreferenceKey, GetLanguageCode(pending));
            ApplyLanguage(pending);
            return;
        }

        string savedLanguage;
        if (PlayerData.TryGetString(player, LanguagePreferenceKey, out savedLanguage) &&
            IsSupportedLanguageCode(savedLanguage))
        {
            _hasManualOverride = true;
            ApplyLanguage(GetLanguageFromCode(savedLanguage));
        }
        else
        {
            _hasManualOverride = false;
            ApplyLanguage(GetLanguageFromCode(VRCPlayerApi.GetCurrentLanguage()));
        }
    }

    public override void OnLanguageChanged(string language)
    {
        if (!_hasManualOverride && _pendingLanguage == -1)
        {
            ApplyLanguage(GetLanguageFromCode(language));
        }
    }

    public void SelectChinese()
    {
        SelectManualLanguage(LanguageChinese);
    }

    public void SelectEnglish()
    {
        SelectManualLanguage(LanguageEnglish);
    }

    private void SelectManualLanguage(int language)
    {
        _hasManualOverride = true;
        ApplyLanguage(language);

        if (_persistenceReady)
        {
            PlayerData.SetString(LanguagePreferenceKey, GetLanguageCode(language));
        }
        else
        {
            _pendingLanguage = language;
        }
    }

    private void ApplyLanguage(int language)
    {
        _currentLanguage = language == LanguageEnglish ? LanguageEnglish : LanguageChinese;

        int textCount = textTargets == null ? 0 : textTargets.Length;
        int keyCount = textKeys == null ? 0 : textKeys.Length;
        int bindingCount = Mathf.Min(textCount, keyCount);
        for (int i = 0; i < bindingCount; i++)
        {
            if (textTargets[i] != null)
            {
                textTargets[i].text = GetText(textKeys[i]);
            }
        }

        int sliderCount = specialValueSliders == null ? 0 : specialValueSliders.Length;
        int sliderKeyCount = specialValueSliderKeys == null ? 0 : specialValueSliderKeys.Length;
        int specialSliderCount = Mathf.Min(sliderCount, sliderKeyCount);
        for (int i = 0; i < specialSliderCount; i++)
        {
            if (specialValueSliders[i] != null)
            {
                specialValueSliders[i].SetSpecialValueText(GetText(specialValueSliderKeys[i]));
            }
        }

        int duoCount = specialValueDuoSliders == null ? 0 : specialValueDuoSliders.Length;
        int duoKeyACount = specialValueDuoSliderKeysA == null ? 0 : specialValueDuoSliderKeysA.Length;
        int duoKeyBCount = specialValueDuoSliderKeysB == null ? 0 : specialValueDuoSliderKeysB.Length;
        int specialDuoCount = Mathf.Min(duoCount, Mathf.Min(duoKeyACount, duoKeyBCount));
        for (int i = 0; i < specialDuoCount; i++)
        {
            if (specialValueDuoSliders[i] != null)
            {
                specialValueDuoSliders[i].SetSpecialValueTexts(
                    GetText(specialValueDuoSliderKeysA[i]),
                    GetText(specialValueDuoSliderKeysB[i]));
            }
        }

        if (chineseButton != null) chineseButton.interactable = _currentLanguage != LanguageChinese;
        if (englishButton != null) englishButton.interactable = _currentLanguage != LanguageEnglish;

        if (ctrlPanel != null) ctrlPanel.RefreshLocalizedText();
        if (syncManager != null) syncManager.RefreshLocalizedText();
        if (documentationController != null) documentationController.RefreshLocalizedText();
    }

    public string GetText(string key)
    {
        if (string.IsNullOrEmpty(key) || keys == null) return "";

        string[] selectedTexts = _currentLanguage == LanguageEnglish ? englishTexts : zhCNTexts;
        int selectedCount = selectedTexts == null ? 0 : selectedTexts.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != key) continue;

            if (i < selectedCount && !string.IsNullOrEmpty(selectedTexts[i]))
            {
                return selectedTexts[i];
            }

            if (zhCNTexts != null && i < zhCNTexts.Length && !string.IsNullOrEmpty(zhCNTexts[i]))
            {
                return zhCNTexts[i];
            }

            return "[" + key + "]";
        }

        return "[" + key + "]";
    }

    private int GetLanguageFromCode(string language)
    {
        return !string.IsNullOrEmpty(language) && language.StartsWith("zh")
            ? LanguageChinese
            : LanguageEnglish;
    }

    private bool IsSupportedLanguageCode(string language)
    {
        return language == "zh-CN" || language == "en";
    }

    private string GetLanguageCode(int language)
    {
        return language == LanguageEnglish ? "en" : "zh-CN";
    }
}
