using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DocumentationController : UdonSharpBehaviour
{
    [Header("Navigation")]
    public GameObject mainMenuRoot;
    public GameObject documentRoot;
    public Button previousButton;
    public Button nextButton;

    [Header("Page Content")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI pageIndicatorText;
    public Image pageImage;
    public string[] pageTitleKeys;
    public string[] pageBodyKeys;
    public Sprite[] pageImages;

    [Header("Localization")]
    public LocalizationManager localizationManager;

    private int _currentPage;

    private void Start()
    {
        if (documentRoot != null) documentRoot.SetActive(false);
        RefreshPage();
    }

    public void OpenDocument()
    {
        _currentPage = 0;
        if (mainMenuRoot != null) mainMenuRoot.SetActive(false);
        if (documentRoot != null) documentRoot.SetActive(true);
        RefreshPage();
    }

    public void CloseDocument()
    {
        if (documentRoot != null) documentRoot.SetActive(false);
        if (mainMenuRoot != null) mainMenuRoot.SetActive(true);
    }

    public void PreviousPage()
    {
        if (_currentPage <= 0) return;
        _currentPage--;
        RefreshPage();
    }

    public void NextPage()
    {
        int pageCount = GetPageCount();
        if (_currentPage >= pageCount - 1) return;
        _currentPage++;
        RefreshPage();
    }

    public void RefreshLocalizedText()
    {
        RefreshPage();
    }

    private int GetPageCount()
    {
        int titleCount = pageTitleKeys == null ? 0 : pageTitleKeys.Length;
        int bodyCount = pageBodyKeys == null ? 0 : pageBodyKeys.Length;
        return Mathf.Min(titleCount, bodyCount);
    }

    private void RefreshPage()
    {
        int pageCount = GetPageCount();
        if (pageCount <= 0)
        {
            if (titleText != null) titleText.text = "";
            if (bodyText != null) bodyText.text = "";
            if (pageIndicatorText != null) pageIndicatorText.text = "0 / 0";
            if (pageImage != null) pageImage.gameObject.SetActive(false);
            if (previousButton != null) previousButton.interactable = false;
            if (nextButton != null) nextButton.interactable = false;
            return;
        }

        _currentPage = Mathf.Clamp(_currentPage, 0, pageCount - 1);

        if (titleText != null)
        {
            titleText.text = GetLocalizedText(pageTitleKeys[_currentPage]);
        }

        if (bodyText != null)
        {
            bodyText.text = GetLocalizedText(pageBodyKeys[_currentPage]);
        }

        if (pageIndicatorText != null)
        {
            pageIndicatorText.text = (_currentPage + 1) + " / " + pageCount;
        }

        if (pageImage != null)
        {
            Sprite image = pageImages != null && _currentPage < pageImages.Length
                ? pageImages[_currentPage]
                : null;
            pageImage.sprite = image;
            pageImage.gameObject.SetActive(image != null);
        }

        if (previousButton != null) previousButton.interactable = _currentPage > 0;
        if (nextButton != null) nextButton.interactable = _currentPage < pageCount - 1;
    }

    private string GetLocalizedText(string key)
    {
        return localizationManager != null ? localizationManager.GetText(key) : "[" + key + "]";
    }
}
