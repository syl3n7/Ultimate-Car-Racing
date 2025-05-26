using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

/// <summary>
/// Quick setup script to create a loading screen UI in your scene.
/// This script can be run in the editor to automatically create all necessary UI components.
/// </summary>
public class LoadingScreenSetup : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Setup Options")]
    public bool setupLoadingScreen = false;
    public Canvas targetCanvas;
    
    void Start()
    {
        if (setupLoadingScreen && targetCanvas != null)
        {
            CreateLoadingScreenUI();
            setupLoadingScreen = false; // Prevent multiple setups
        }
    }
    
    [ContextMenu("Create Loading Screen UI")]
    public void CreateLoadingScreenUI()
    {
        if (targetCanvas == null)
        {
            Debug.LogError("Please assign a target Canvas first!");
            return;
        }
        
        // Create main loading panel
        GameObject loadingPanel = new GameObject("LoadingPanel");
        loadingPanel.transform.SetParent(targetCanvas.transform, false);
        
        // Add and configure RectTransform
        RectTransform panelRect = loadingPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Add background image
        Image backgroundImage = loadingPanel.AddComponent<Image>();
        backgroundImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f); // Dark semi-transparent
        
        // Create main text
        GameObject loadingTextObj = new GameObject("LoadingText");
        loadingTextObj.transform.SetParent(loadingPanel.transform, false);
        
        TextMeshProUGUI loadingText = loadingTextObj.AddComponent<TextMeshProUGUI>();
        loadingText.text = "Loading...";
        loadingText.fontSize = 48;
        loadingText.color = Color.white;
        loadingText.alignment = TextAlignmentOptions.Center;
        
        RectTransform textRect = loadingTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.7f);
        textRect.anchorMax = new Vector2(0.5f, 0.7f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(400, 100);
        
        // Create progress bar background
        GameObject progressBgObj = new GameObject("ProgressBarBackground");
        progressBgObj.transform.SetParent(loadingPanel.transform, false);
        
        Image progressBg = progressBgObj.AddComponent<Image>();
        progressBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        RectTransform progressBgRect = progressBgObj.GetComponent<RectTransform>();
        progressBgRect.anchorMin = new Vector2(0.5f, 0.5f);
        progressBgRect.anchorMax = new Vector2(0.5f, 0.5f);
        progressBgRect.anchoredPosition = Vector2.zero;
        progressBgRect.sizeDelta = new Vector2(600, 20);
        
        // Create progress bar
        GameObject progressBarObj = new GameObject("ProgressBar");
        progressBarObj.transform.SetParent(loadingPanel.transform, false);
        
        Slider progressSlider = progressBarObj.AddComponent<Slider>();
        progressSlider.minValue = 0f;
        progressSlider.maxValue = 1f;
        progressSlider.value = 0f;
        
        RectTransform sliderRect = progressBarObj.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = Vector2.zero;
        sliderRect.sizeDelta = new Vector2(600, 20);
        
        // Create slider background
        GameObject sliderBackground = new GameObject("Background");
        sliderBackground.transform.SetParent(progressBarObj.transform, false);
        Image sliderBgImage = sliderBackground.AddComponent<Image>();
        sliderBgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        RectTransform sliderBgRect = sliderBackground.GetComponent<RectTransform>();
        sliderBgRect.anchorMin = Vector2.zero;
        sliderBgRect.anchorMax = Vector2.one;
        sliderBgRect.offsetMin = Vector2.zero;
        sliderBgRect.offsetMax = Vector2.zero;
        
        // Create slider fill area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(progressBarObj.transform, false);
        
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;
        
        // Create slider fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.3f, 0.7f, 1f, 1f); // Light blue
        
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        
        // Configure slider
        progressSlider.targetGraphic = fillImage;
        progressSlider.fillRect = fillRect;
        
        // Create tip text
        GameObject tipTextObj = new GameObject("TipText");
        tipTextObj.transform.SetParent(loadingPanel.transform, false);
        
        TextMeshProUGUI tipText = tipTextObj.AddComponent<TextMeshProUGUI>();
        tipText.text = "Tip: Hold shift to brake harder and get better control";
        tipText.fontSize = 24;
        tipText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        tipText.alignment = TextAlignmentOptions.Center;
        
        RectTransform tipRect = tipTextObj.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(0.5f, 0.3f);
        tipRect.anchorMax = new Vector2(0.5f, 0.3f);
        tipRect.anchoredPosition = Vector2.zero;
        tipRect.sizeDelta = new Vector2(800, 60);
        
        // Make sure it's initially hidden
        loadingPanel.SetActive(false);
        
        // Try to assign to UIManager if it exists
        UIManager uiManager = FindObjectOfType<UIManager>();
        if (uiManager != null)
        {
            // You'll need to manually assign these in the inspector, but this helps identify them
            Debug.Log("Loading screen created! Please assign the following components to UIManager:");
            Debug.Log($"loadingPanel: {loadingPanel.name}");
            Debug.Log($"loadingText: {loadingText.name}");
            Debug.Log($"loadingProgressBar: {progressSlider.name}");
            Debug.Log($"loadingTips: {tipText.name}");
        }
        
        Debug.Log("Loading screen UI created successfully!");
    }
#endif
}

// Editor script for easier setup
#if UNITY_EDITOR
[CustomEditor(typeof(LoadingScreenSetup))]
public class LoadingScreenSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        LoadingScreenSetup setup = (LoadingScreenSetup)target;
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Create Loading Screen UI"))
        {
            setup.CreateLoadingScreenUI();
        }
        
        GUILayout.Space(5);
        
        if (GUILayout.Button("Find Canvas Automatically"))
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                setup.targetCanvas = canvas;
                Debug.Log($"Found and assigned Canvas: {canvas.name}");
            }
            else
            {
                Debug.LogWarning("No Canvas found in scene!");
            }
        }
    }
}
#endif
