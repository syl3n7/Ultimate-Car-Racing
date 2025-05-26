using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Simple loading screen manager for smooth scene transitions.
/// This script provides a clean, fast loading screen with progress bar and tips.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    [Header("Loading Screen Components")]
    public GameObject loadingPanel;
    public TextMeshProUGUI loadingText;
    public Slider progressBar;
    public TextMeshProUGUI tipText;
    public CanvasGroup loadingCanvasGroup;
    
    [Header("Settings")]
    public float fadeSpeed = 2f;
    public bool showRandomTips = true;
    
    private static LoadingScreenManager instance;
    public static LoadingScreenManager Instance => instance;
    
    private readonly string[] loadingTips = {
        "Hold shift to brake harder and get better control",
        "Use manual transmission for better acceleration",
        "Bank into turns for realistic driving physics",
        "Watch your RPM meter to optimize gear shifts",
        "The camera automatically follows your driving style",
        "Higher gears give better top speed but slower acceleration",
        "Use the mouse to look around while driving",
        "Practice makes perfect - try different tracks!",
        "Network play supports up to 20 players per room",
        "Your car's physics respond realistically to input"
    };
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize loading screen as hidden
            if (loadingPanel != null)
                loadingPanel.SetActive(false);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Show the loading screen with optional message
    /// </summary>
    public void ShowLoadingScreen(string message = "Loading...")
    {
        if (loadingPanel == null) return;
        
        loadingPanel.SetActive(true);
        
        if (loadingText != null)
            loadingText.text = message;
            
        if (progressBar != null)
            progressBar.value = 0f;
            
        if (tipText != null && showRandomTips)
            DisplayRandomTip();
            
        // Fade in
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.alpha = 0f;
            StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 1f, fadeSpeed));
        }
    }
    
    /// <summary>
    /// Update the loading progress
    /// </summary>
    public void UpdateProgress(float progress, string message = null)
    {
        if (!loadingPanel.activeSelf) return;
        
        if (progressBar != null)
            progressBar.value = Mathf.Clamp01(progress);
            
        if (!string.IsNullOrEmpty(message) && loadingText != null)
            loadingText.text = message;
    }
    
    /// <summary>
    /// Hide the loading screen
    /// </summary>
    public void HideLoadingScreen()
    {
        if (loadingPanel == null) return;
        
        if (loadingCanvasGroup != null)
        {
            StartCoroutine(FadeOutAndHide());
        }
        else
        {
            loadingPanel.SetActive(false);
        }
    }
    
    private void DisplayRandomTip()
    {
        if (tipText != null && loadingTips.Length > 0)
        {
            string randomTip = loadingTips[Random.Range(0, loadingTips.Length)];
            tipText.text = "Tip: " + randomTip;
        }
    }
    
    private IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float targetAlpha, float speed)
    {
        while (Mathf.Abs(canvasGroup.alpha - targetAlpha) > 0.01f)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, speed * Time.deltaTime);
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;
    }
    
    private IEnumerator FadeOutAndHide()
    {
        yield return StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 0f, fadeSpeed));
        loadingPanel.SetActive(false);
    }
    
    /// <summary>
    /// Convenience method for scene loading with loading screen
    /// </summary>
    public static IEnumerator LoadSceneAsync(string sceneName, string loadingMessage = null)
    {
        if (Instance == null)
        {
            Debug.LogError("LoadingScreenManager instance not found!");
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
            yield break;
        }
        
        // Show loading screen
        string message = string.IsNullOrEmpty(loadingMessage) ? $"Loading {sceneName}..." : loadingMessage;
        Instance.ShowLoadingScreen(message);
        
        // Small delay to ensure loading screen shows
        yield return new WaitForSeconds(0.1f);
        
        // Start loading the scene
        var asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        
        // Update progress
        while (!asyncLoad.isDone)
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            Instance.UpdateProgress(progress, $"{message} {(progress * 100):0}%");
            yield return null;
        }
        
        // Show completion
        Instance.UpdateProgress(1f, "Loading Complete!");
        yield return new WaitForSeconds(0.3f);
        
        // Hide loading screen
        Instance.HideLoadingScreen();
    }
}
