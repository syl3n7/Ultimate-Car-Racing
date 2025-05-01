using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltimateCarRacing.Networking;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }
    
    [SerializeField] private GameObject loadingScreen;
    [SerializeField] private float minimumLoadingTime = 1.5f; // Minimum time to show loading screen
    
    private string targetScene;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            if (loadingScreen != null)
            {
                loadingScreen.SetActive(false);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        NetworkManager.Instance.OnMessageReceived += HandleNetworkMessage;
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived -= HandleNetworkMessage;
        }
    }
    
    private void HandleNetworkMessage(string fromClient, string message)
    {
        string[] parts = message.Split('|');
        if (parts.Length < 1) return;
        
        string command = parts[0];
        
        if (command == "LOAD_SCENE" && parts.Length > 1)
        {
            string sceneName = parts[1];
            LoadScene(sceneName);
        }
    }
    
    public void LoadScene(string sceneName)
    {
        targetScene = sceneName;
        StartCoroutine(LoadSceneAsync(sceneName));
    }
    
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        // Show loading screen
        if (loadingScreen != null)
        {
            loadingScreen.SetActive(true);
        }
        
        // Start timer for minimum loading time
        float startTime = Time.time;
        
        // Start loading the scene
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;
        
        // Wait until the scene is fully loaded
        while (!asyncLoad.isDone)
        {
            // Calculate how much time has passed
            float elapsedTime = Time.time - startTime;
            
            // Check if both the scene is ready and minimum time has passed
            if (asyncLoad.progress >= 0.9f && elapsedTime >= minimumLoadingTime)
            {
                asyncLoad.allowSceneActivation = true;
            }
            
            yield return null;
        }
        
        // Scene is loaded, wait one frame
        yield return null;
        
        // Hide loading screen
        if (loadingScreen != null)
        {
            loadingScreen.SetActive(false);
        }
        
        // If we loaded the game scene, make sure necessary objects are present
        if (sceneName == "GameOn")
        {
            EnsureGameManagerExists();
        }
    }
    
    private void EnsureGameManagerExists()
    {
        // Check if GameManager exists in the scene
        if (FindObjectOfType<GameManager>() == null)
        {
            // Create GameManager if it doesn't exist
            GameObject gameManagerPrefab = Resources.Load<GameObject>("Prefabs/GameManager");
            if (gameManagerPrefab != null)
            {
                Instantiate(gameManagerPrefab);
            }
            else
            {
                Debug.LogError("GameManager prefab not found in Resources/Prefabs/");
            }
        }
    }
}