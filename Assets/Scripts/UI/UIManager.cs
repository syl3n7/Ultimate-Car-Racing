using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.IO;
using Newtonsoft.Json;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    
    [Header("Main Menu Panels")]
    public GameObject mainMenuPanel;
    public GameObject instructionsPanel;
    public GameObject creditsPanel;
    public GameObject profilePanel;
    public GameObject multiplayerPanel;
    public GameObject roomListPanel;
    public GameObject roomLobbyPanel;
    
    [Header("Profile UI")]
    public TMP_InputField playerNameInput;
    public Transform profileListContent;
    public GameObject profileListItemPrefab;
    public TextMeshProUGUI currentProfileText;
    
    [Header("Room List UI")]
    public Transform roomListContent;
    public GameObject roomListItemPrefab;
    public Button refreshRoomsButton;
    public TMP_InputField createRoomNameInput;
    public Slider maxPlayersSlider;
    public TextMeshProUGUI maxPlayersText;
    
    [Header("Room Lobby UI")]
    public TextMeshProUGUI roomInfoText;
    public Transform playerListContent;
    public GameObject playerListItemPrefab;
    public Button startGameButton;
    public Button leaveGameButton;
    public TextMeshProUGUI playerCountText;
    
    [Header("Connection UI")]
    public GameObject connectionPanel;
    public TextMeshProUGUI connectionStatusText;
    
    [Header("Notification UI")]
    public GameObject notificationPanel;
    public TextMeshProUGUI notificationText;

    [Header("Loading Screen")]
    public GameObject loadingPanel;
    public TextMeshProUGUI loadingText;
    public Slider loadingProgressBar;
    public TextMeshProUGUI loadingTips;

    [Header("Authentication UI")]
    public GameObject authPanel;
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public Button loginButton;
    public TextMeshProUGUI authStatusText;
    
    [Header("Additional UI Panels")]
    public GameObject networkInfoPanel;
    public GameObject extraInfoHUD;
    public GameObject raceHUD;
    public GameObject consolePanel;
    
    [Header("Button References")]
    [Header("Main Menu Buttons")]
    public Button playButton;
    public Button instructionsButton;
    public Button creditsButton;
    public Button profileButton;
    public Button exitButton;
    
    [Header("Profile Panel Buttons")]
    public Button createProfileButton;
    public Button backToMainButton;
    
    [Header("Instructions Panel Buttons")]
    public Button backFromInstructionsButton;
    
    [Header("Multiplayer Panel Buttons")]
    public Button createGameButton;
    public Button joinGameButton;
    public Button backFromMultiplayerButton;
    
    [Header("Room List Panel Buttons")]
    public Button createRoomButton;
    public Button joinRoomButton;
    public Button backFromRoomListButton;
    
    [Header("Credits Panel Buttons")]
    public Button backFromCreditsButton;
    
    [Header("Profile Panel Buttons - Additional")]
    public Button backFromProfileButton;
    
    [Header("Room Lobby Panel Buttons")]
    public Button backFromRoomLobbyButton;
    
    [Header("Car UI")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI rpmText;
    public TextMeshProUGUI gearText;
    public bool showKMH = true;
    public bool showGear = true;
    public string speedFormat = "0";
    public string rpmFormat = "0";
    
    [Header("Network Stats")]
    public TextMeshProUGUI latencyText;
    private float lastLatencyUpdateTime = 0f;
    private const float LATENCY_UPDATE_INTERVAL = 1f; // Update latency every second

    [Header("Race UI")]
    public GameObject raceUIPanel;        // Panel containing car stats and network info
    public GameObject networkStatsPanel;  // Panel specifically for network stats
    private bool isRaceUIVisible = false;

    private CarController playerCarController;
    private bool carUIInitialized = false;
    
    // Player profile data
    private string playerName = "Player";
    private string playerId;
    private List<ProfileData> savedProfiles = new List<ProfileData>();
    
    // Room management
    private List<Dictionary<string, object>> roomList = new List<Dictionary<string, object>>();
    private List<string> playersInRoom = new List<string>();
    private string currentRoomId;
    private string currentRoomName;
    private bool isHost = false;
    
    [Serializable]
    public class ProfileData
    {
        public string name;
        public string id;
        public string lastPlayed;
        
        public ProfileData(string name, string id)
        {
            this.name = name;
            this.id = id;
            this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
    
    [Serializable]
    public class ProfileList
    {
        public List<ProfileData> profiles = new List<ProfileData>();
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Register for scene load events to handle UI visibility
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        // Initialize UI
        HideAllPanels();
        ShowMainMenu();
        
        // Load saved profiles
        LoadProfiles();
        
        // Auto-connect all UI buttons
        ConnectAllUIButtons();
        
        // Register for network events
        SecureNetworkManager networkManager = SecureNetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected += (msg) => OnConnected();
            networkManager.OnDisconnected += (msg) => OnDisconnected();
            networkManager.OnConnectionFailed += (msg) => OnConnectionFailed();
            networkManager.OnRoomListReceived += OnRoomListReceived;
            networkManager.OnGameHosted += OnGameHosted;
            networkManager.OnRoomJoined += OnJoinedGame;
            networkManager.OnPlayerJoined += OnPlayerJoined;
            networkManager.OnPlayerDisconnected += OnPlayerLeftRoom;
            networkManager.OnGameStarted += OnGameStarted;
            networkManager.OnServerMessage += OnServerMessage;
            networkManager.OnRoomPlayersReceived += OnRoomPlayersReceived;
        }

        // Initialize max players text
        OnMaxPlayersSliderChanged(maxPlayersSlider.value);

        // Connect auth panel buttons
        if (loginButton != null)
        {
            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(OnLoginButtonClicked);
        }
    }
    
    // Connect all UI buttons using Inspector references
    private void ConnectAllUIButtons()
    {
        // Main Menu buttons
        ConnectButtonDirect(playButton, OnPlayButtonClicked, "PlayButton");
        ConnectButtonDirect(instructionsButton, ShowInstructions, "InstructionsButton");
        ConnectButtonDirect(creditsButton, ShowCredits, "CreditsButton");
        ConnectButtonDirect(profileButton, ShowProfilePanel, "ProfileButton");
        ConnectButtonDirect(exitButton, ExitGame, "ExitButton");
        
        // Profile panel buttons
        ConnectButtonDirect(createProfileButton, CreateNewProfile, "CreateProfileButton");
        ConnectButtonDirect(backToMainButton, ShowMainMenu, "BackToMainButton");
        
        // Instructions panel buttons
        ConnectButtonDirect(backFromInstructionsButton, BackFromInstructions, "BackFromInstructionsButton");
        
        // Credits panel buttons
        ConnectButtonDirect(backFromCreditsButton, BackFromCredits, "BackFromCreditsButton");
        
        // Profile panel buttons (additional)
        ConnectButtonDirect(backFromProfileButton, BackFromProfile, "BackFromProfileButton");
        
        // Multiplayer panel buttons
        ConnectButtonDirect(createGameButton, ShowRoomListPanel, "CreateGameButton");
        ConnectButtonDirect(joinGameButton, ShowRoomListPanel, "JoinGameButton");
        ConnectButtonDirect(backFromMultiplayerButton, BackFromMultiplayer, "BackFromMultiplayerButton");
        
        // Room list panel buttons
        ConnectButtonDirect(createRoomButton, CreateRoom, "CreateRoomButton");
        ConnectButtonDirect(joinRoomButton, JoinSelectedRoom, "JoinRoomButton");
        ConnectButtonDirect(refreshRoomsButton, RefreshRoomList, "RefreshRoomsButton");
        ConnectButtonDirect(backFromRoomListButton, BackFromRoomList, "BackFromRoomListButton");
        
        // Room lobby panel buttons
        ConnectButtonDirect(startGameButton, StartGame, "StartGameButton");
        ConnectButtonDirect(leaveGameButton, LeaveRoom, "LeaveGameButton");
        ConnectButtonDirect(backFromRoomLobbyButton, BackFromRoomLobby, "BackFromRoomLobbyButton");
        
        // Auth panel buttons
        ConnectButtonDirect(loginButton, OnLoginButtonClicked, "LoginButton");
        
        // Connect max players slider if assigned
        if (maxPlayersSlider != null)
        {
            maxPlayersSlider.onValueChanged.RemoveAllListeners();
            maxPlayersSlider.onValueChanged.AddListener(OnMaxPlayersSliderChanged);
            maxPlayersSlider.maxValue = 20;
            OnMaxPlayersSliderChanged(maxPlayersSlider.value);
        }
    }



    // Helper method for direct button connections using Inspector references
    private void ConnectButtonDirect(Button button, UnityEngine.Events.UnityAction action, string buttonName)
    {
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
    
    // In OnDestroy method - replace NetworkClient references with SecureNetworkManager
    void OnDestroy()
    {
        // Unregister network events
        SecureNetworkManager networkManager = SecureNetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected -= (msg) => OnConnected();
            networkManager.OnDisconnected -= (msg) => OnDisconnected();
            networkManager.OnConnectionFailed -= (msg) => OnConnectionFailed();
            networkManager.OnRoomListReceived -= OnRoomListReceived;
            networkManager.OnGameHosted -= OnGameHosted;
            networkManager.OnRoomJoined -= OnJoinedGame;
            networkManager.OnPlayerJoined -= OnPlayerJoined;
            networkManager.OnPlayerDisconnected -= OnPlayerLeftRoom;
            networkManager.OnGameStarted -= OnGameStarted;
            networkManager.OnServerMessage -= OnServerMessage;
            networkManager.OnRoomPlayersReceived -= OnRoomPlayersReceived;
        }
        
        // Unregister scene loading event
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    #region UI Navigation
    
    public void ShowMainMenu()
    {
        HideAllPanels();
        mainMenuPanel.SetActive(true);
    }
    
    public void ShowInstructions()
    {
        HideAllPanels();
        instructionsPanel.SetActive(true);
    }
    
    public void ShowCredits()
    {
        HideAllPanels();
        creditsPanel.SetActive(true);
    }
    
    public void ShowProfilePanel()
    {
        HideAllPanels();
        profilePanel.SetActive(true);
        
        // Refresh profile list
        RefreshProfileList();
    }
    
    public void ShowMultiplayerPanel()
    {
        // Must have a profile to play
        if (string.IsNullOrEmpty(playerId))
        {
            ShowProfilePanel();
            ShowNotification("Please create or select a profile first");
            return;
        }
        
        HideAllPanels();
        multiplayerPanel.SetActive(true);
        
        // Connect to server if not already connected
        if (SecureNetworkManager.Instance != null && !SecureNetworkManager.Instance.IsConnected())
        {
            ShowConnectionPanel("Connecting to server...");
            _ = SecureNetworkManager.Instance.Connect();
        }
    }
    public void OnPlayButtonClicked()
{
    // Check if player has a profile
    if (string.IsNullOrEmpty(playerId))
    {
        // No profile exists, direct to profile panel
        ShowProfilePanel();
        ShowNotification("Please create or select a profile to play");
        return;
    }
    
    // If SecureNetworkManager exists, check if authenticated
    if (SecureNetworkManager.Instance != null && !SecureNetworkManager.Instance.IsAuthenticated())
    {
        // Show authentication panel first
        ShowAuthPanel();
        ShowNotification("Please log in to continue");
        return;
    }
    
    // Profile exists and authenticated, go to multiplayer panel
    ShowMultiplayerPanel();
}
    public void ShowRoomListPanel()
    {
        HideAllPanels();
        roomListPanel.SetActive(true);
        
        // Set default room name
        createRoomNameInput.text = $"{playerName}'s Room";
        
        // Clear previous room list
        ClearRoomList();
        
        // Request fresh room list from server
        RefreshRoomList();
    }
    
    public void ShowRoomLobbyPanel()
    {
        // Check if the panel exists
        if (roomLobbyPanel == null)
        {
            return;
        }
        
        HideAllPanels();
        roomLobbyPanel.SetActive(true);
        
        // Update room info
        UpdateRoomInfo();
    }
    
    public void ShowConnectionPanel(string status)
    {
        connectionPanel.SetActive(true);
        connectionStatusText.text = status;
    }
    
    public void HideConnectionPanel()
    {
        connectionPanel.SetActive(false);
    }
    
    public void ShowLoadingScreen(string message = "Loading...")
    {
        if (loadingPanel != null)
        {
            HideAllPanels();
            loadingPanel.SetActive(true);
            
            if (loadingText != null)
                loadingText.text = message;
                
            if (loadingProgressBar != null)
                loadingProgressBar.value = 0f;
                
            if (loadingTips != null)
                ShowRandomLoadingTip();
        }
    }
    
    public void UpdateLoadingProgress(float progress, string message = null)
    {
        if (loadingPanel != null && loadingPanel.activeSelf)
        {
            if (loadingProgressBar != null)
                loadingProgressBar.value = Mathf.Clamp01(progress);
                
            if (!string.IsNullOrEmpty(message) && loadingText != null)
                loadingText.text = message;
        }
    }
    
    public void HideLoadingScreen()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }
    }
    
    private void ShowRandomLoadingTip()
    {
        if (loadingTips == null) return;
        
        string[] tips = {
            "Hold shift to brake harder and get better control",
            "Use manual transmission for better acceleration",
            "Bank into turns for realistic driving physics",
            "Watch your RPM meter to optimize gear shifts",
            "The camera automatically follows your driving style",
            "Higher gears give better top speed but slower acceleration"
        };
        
        loadingTips.text = "Tip: " + tips[UnityEngine.Random.Range(0, tips.Length)];
    }
    
    public void ShowNotification(string message, float duration = 3f)
    {
        notificationPanel.SetActive(true);
        notificationText.text = message;
        
        StartCoroutine(HideNotificationAfterDelay(duration));
    }
    
    private IEnumerator HideNotificationAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        notificationPanel.SetActive(false);
    }
    
    private void HideAllPanels()
    {
        mainMenuPanel.SetActive(false);
        instructionsPanel.SetActive(false);
        creditsPanel.SetActive(false);
        profilePanel.SetActive(false);
        multiplayerPanel.SetActive(false);
        roomListPanel.SetActive(false);
        roomLobbyPanel.SetActive(false);
        connectionPanel.SetActive(false);
        notificationPanel.SetActive(false);
        
        // Hide auth panel if it exists
        if (authPanel != null)
        {
            authPanel.SetActive(false);
        }
        
        // Hide loading panel if it exists
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }
        
        // Hide additional UI panels if they exist
        if (networkInfoPanel != null)
        {
            networkInfoPanel.SetActive(false);
        }
        
        if (extraInfoHUD != null)
        {
            extraInfoHUD.SetActive(false);
        }
        
        if (raceHUD != null)
        {
            raceHUD.SetActive(false);
        }
        
        if (consolePanel != null)
        {
            consolePanel.SetActive(false);
        }
        
        // Don't hide race UI panels here - they're controlled by scene changes
    }

    public void ShowAuthPanel(string message = "Please login to continue")
    {
        if (authPanel == null)
        {
            return;
        }
        
        HideAllPanels();
        authPanel.SetActive(true);
        
        if (authStatusText != null)
        {
            authStatusText.text = message;
        }
        
        // Pre-fill the username if we have a profile selected
        if (usernameInput != null && !string.IsNullOrEmpty(playerName))
        {
            usernameInput.text = playerName;
        }
    }
    
    public void OnLoginButtonClicked()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;
        
        if (string.IsNullOrEmpty(username))
        {
            ShowNotification("Please enter a username");
            return;
        }
        
        if (string.IsNullOrEmpty(password))
        {
            ShowNotification("Please enter a password");
            return;
        }
        
        if (SecureNetworkManager.Instance != null)
        {
            // Set the credentials in NetworkManager
            SecureNetworkManager.Instance.SetCredentials(username, password);
            
            // Update local player name and attempt to reconnect
            playerName = username;
            
            // Show connection panel
            ShowConnectionPanel("Authenticating...");
            
            // If not connected, connect
            if (!SecureNetworkManager.Instance.IsConnected())
            {
                _ = SecureNetworkManager.Instance.Connect();
            }
        }
    }
    
    // Add explicit method for back button functionality
    public void BackFromMultiplayer()
    {
        ShowMainMenu();
    }
    
    public void BackFromInstructions()
    {
        ShowMainMenu();
    }
    
    public void BackFromCredits()
    {
        ShowMainMenu();
    }
    
    public void BackFromProfile()
    {
        ShowMainMenu();
    }
    
    public void BackFromRoomList()
    {
        ShowMultiplayerPanel();
    }
    
    public void BackFromRoomLobby()
    {
        ShowRoomListPanel();
    }
    
    #endregion
    
    #region Profile Management
    
    public void CreateNewProfile()
    {
        string name = playerNameInput.text;
        if (string.IsNullOrEmpty(name))
        {
            ShowNotification("Please enter a name");
            return;
        }
        
        // Generate a player ID
        string id = GenerateUniquePlayerId(name);
        
        // Create new profile
        ProfileData profile = new ProfileData(name, id);
        savedProfiles.Add(profile);
        
        // Save to disk
        SaveProfiles();
        
        // Set as current profile
        SelectProfile(profile);
        
        // Show multiplayer panel
        ShowMultiplayerPanel();
    }
    
    private string GenerateUniquePlayerId(string name)
    {
        // Create a unique ID combining name, timestamp, and some system info
        string systemInfo = SystemInfo.deviceUniqueIdentifier;
        string timestamp = DateTime.Now.Ticks.ToString();
        string rawId = name + systemInfo + timestamp;
        
        // Use simple hash for a shorter ID
        return Math.Abs(rawId.GetHashCode()).ToString();
    }
    
    private void SelectProfile(ProfileData profile)
    {
        playerName = profile.name;
        playerId = profile.id;
        
        // Update UI
        currentProfileText.text = $"Profile: {playerName}";
        
        // Update profile's last played time
        profile.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SaveProfiles();
        
        ShowNotification($"Welcome, {playerName}!");
    }
    
private void RefreshProfileList()
{
    // Check if profileListContent is assigned
    if (profileListContent == null)
    {
        return;
    }
    
    // Check if profileListItemPrefab is assigned
    if (profileListItemPrefab == null)
    {
        return;
    }
    
    // Clear existing items
    foreach (Transform child in profileListContent)
    {
        Destroy(child.gameObject);
    }
    
    // Add profiles
    foreach (ProfileData profile in savedProfiles)
    {
        GameObject profileItem = Instantiate(profileListItemPrefab, profileListContent);
        
        // Check for required child objects
        Transform nameTextTransform = profileItem.transform.Find("NameText");
        if (nameTextTransform == null)
        {
            continue;
        }
        
        TextMeshProUGUI nameText = nameTextTransform.GetComponent<TextMeshProUGUI>();
        if (nameText == null)
        {
            continue;
        }
        nameText.text = profile.name;
        
        Transform infoTextTransform = profileItem.transform.Find("InfoText");
        if (infoTextTransform == null)
        {
            continue;
        }
        
        TextMeshProUGUI infoText = infoTextTransform.GetComponent<TextMeshProUGUI>();
        if (infoText == null)
        {
            continue;
        }
        infoText.text = $"Last played: {profile.lastPlayed}";
        
        // Set button callback
        Button selectButton = profileItem.GetComponent<Button>();
        if (selectButton != null)
        {
            ProfileData profileCopy = profile; // Create a copy for the closure
            selectButton.onClick.AddListener(() => {
                SelectProfile(profileCopy);
                ShowMultiplayerPanel();
            });
        }
    }
}
    
    private void SaveProfiles()
    {
        ProfileList profileList = new ProfileList { profiles = savedProfiles };
        string json = JsonUtility.ToJson(profileList);
        
        string path = Path.Combine(Application.persistentDataPath, "profiles.json");
        File.WriteAllText(path, json);
    }
    
    private void LoadProfiles()
    {
        string path = Path.Combine(Application.persistentDataPath, "profiles.json");
        
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            ProfileList profileList = JsonUtility.FromJson<ProfileList>(json);
            
            if (profileList != null && profileList.profiles != null)
            {
                savedProfiles = profileList.profiles;
            }
        }
    }
    
    #endregion
    
    #region Room Management
    
public async void CreateRoom()
{
    // Make sure we have a valid track selected
    if (GameManager.SelectedTrackIndex < 0)
    {
        GameManager.SelectedTrackIndex = 0; // Set to default track
    }
    
    if (SecureNetworkManager.Instance == null)
    {
        ShowNotification("Network manager not available");
        return;
    }
    
    // Check authentication first
    if (!SecureNetworkManager.Instance.IsAuthenticated())
    {
        ShowNotification("Please log in before creating a room");
        ShowAuthPanel();
        return;
    }
    
    // Show connection panel immediately to provide feedback
    ShowConnectionPanel("Creating room...");
    
    // Make sure we're connected before attempting to create a room
    if (!SecureNetworkManager.Instance.IsConnected())
    {
        ShowConnectionPanel("Connecting to server...");
        
        try {
            // Connect() returns a Task, not a bool
            await SecureNetworkManager.Instance.Connect();
            
            // Check if we're connected after the await
            if (!SecureNetworkManager.Instance.IsConnected()) {
                ShowNotification("Could not connect to server");
                HideConnectionPanel();
                return;
            }
        }
        catch (Exception e) {
            ShowNotification("Connection error: " + e.Message);
            HideConnectionPanel();
            return;
        }
    }
    
    string roomName = createRoomNameInput.text;
    if (string.IsNullOrEmpty(roomName))
        roomName = $"{playerName}'s Room";
        
    int maxPlayers = (int)maxPlayersSlider.value;
    
    try {
        // HostGame is not async and doesn't return a value, it just calls CreateRoom internally
        SecureNetworkManager.Instance.HostGame(roomName, maxPlayers);
        
        // The actual feedback will come through the OnGameHosted event callback
        // We don't need to do anything else here, the event system handles it
    }
    catch (Exception e) {
        ShowNotification("Error creating room: " + e.Message);
        HideConnectionPanel();
    }
}
    
    // In JoinSelectedRoom method - update to use NetworkManager
    public void JoinSelectedRoom()
    {
        if (currentRoomId != null && SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected())
        {
            ShowConnectionPanel("Joining room...");
            SecureNetworkManager.Instance.JoinGame(currentRoomId);
        }
        else
        {
            ShowNotification("Please select a room to join");
        }
    }
    
    // In StartGame method - update to use NetworkManager with server protocol
    public void StartGame()
    {
        if (!isHost)
        {
            // According to SERVER-README.md, only the host can start the game
            ShowNotification("Only the host can start the game");
            return;
        }
        
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected() && !string.IsNullOrEmpty(currentRoomId))
        {
            // Show visual feedback
            ShowConnectionPanel("Starting game...");
            
            // Check if there are any players in the room
            if (playersInRoom.Count <= 0)
            {
                ShowNotification("Cannot start game with no players");
                HideConnectionPanel();
                return;
            }
            
            // Send the start game command according to server documentation
            _ = SecureNetworkManager.Instance.StartGame();
        }
        else
        {
            ShowNotification("Cannot start game - connection issue");
        }
    }
    
    public void LeaveRoom()
    {
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected())
        {
            _ = SecureNetworkManager.Instance.LeaveGame();
            ShowRoomListPanel();
            
            // Reset room state
            currentRoomId = null;
            currentRoomName = null;
            isHost = false;
            playersInRoom.Clear();
        }
    }
    
    public void ExitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    // In RefreshRoomList method - update to use NetworkManager
    public async void RefreshRoomList()
    {
        if (SecureNetworkManager.Instance == null)
        {
            ShowNotification("Network manager not available");
            return;
        }
        
        // Check if we're connected to the server
        if (!SecureNetworkManager.Instance.IsConnected())
        {
            ShowConnectionPanel("Connecting to server...");
            
            try {
                // Connect returns Task, not Task<bool>
                await SecureNetworkManager.Instance.Connect();
                
                // Check connection status after awaiting
                if (!SecureNetworkManager.Instance.IsConnected())
                {
                    ShowNotification("Failed to connect to server");
                    HideConnectionPanel();
                    return;
                }
            }
            catch (Exception e) {
                ShowNotification("Connection error: " + e.Message);
                HideConnectionPanel();
                return;
            }
        }
        
        // We're connected, clear the current list and request updates
        ClearRoomList();
        
        // Show a temporary "Loading..." message
        GameObject loadingText = new GameObject("LoadingText");
        loadingText.transform.SetParent(roomListContent, false);
        TextMeshProUGUI textComponent = loadingText.AddComponent<TextMeshProUGUI>();
        textComponent.text = "Loading rooms...";
        textComponent.fontSize = 24;
        textComponent.alignment = TextAlignmentOptions.Center;
        
        // Request room list from server - this triggers the OnRoomListReceived event callback
        _ = SecureNetworkManager.Instance.RequestRoomList();
    }
    
    private void ClearRoomList()
    {
        foreach (Transform child in roomListContent)
        {
            Destroy(child.gameObject);
        }
    }
    
    private void OnMaxPlayersSliderChanged(float value)
    {
        maxPlayersText.text = $"Max Players: {(int)value}";
    }
    
    private void UpdateRoomInfo()
    {
        // Check references
        if (roomInfoText == null)
        {
            return;
        }
    
        if (playerCountText == null)
        {
            return;
        }
    
        if (startGameButton == null)
        {
            return;
        }
    
        if (playerListContent == null)
        {
            return;
        }
    
        if (playerListItemPrefab == null)
        {
            return;
        }
    
        // Update UI elements
        roomInfoText.text = $"Room: {currentRoomName}";
        playerCountText.text = $"Players: {playersInRoom.Count}";
    
        // Show/hide start game button based on host status
        startGameButton.gameObject.SetActive(isHost);
        
        // Make sure button is properly interactive
        if (isHost)
        {
            startGameButton.interactable = true;
            
            // Add visual indicator that this player is the host
            ColorBlock colors = startGameButton.colors;
            colors.normalColor = new Color(0.8f, 0.9f, 0.8f);
            startGameButton.colors = colors;
        }
    
        // Clear player list
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }
    
        // Populate player list
        foreach (string playerId in playersInRoom)
        {
            GameObject playerItem = Instantiate(playerListItemPrefab, playerListContent);
            TextMeshProUGUI playerText = playerItem.GetComponentInChildren<TextMeshProUGUI>();
    
            if (playerText == null)
            {
                continue;
            }            
            string playerDisplayName = playerId;
            if (SecureNetworkManager.Instance != null && playerId == SecureNetworkManager.Instance.GetClientId())
                playerDisplayName += " (You)";

            playerText.text = playerDisplayName;
        }
    }
    
    #endregion
    
    #region Network Event Handlers
    
    private void OnConnected()
    {
        HideConnectionPanel();
        
        // According to SERVER-README.md section 3.2, only NAME is needed for registration
        // SecureNetworkManager already sent the NAME command during connection
        
        // Just for extra in-game information, we can use PLAYER_INFO command to get details
        if (SecureNetworkManager.Instance != null)
        {
            _ = SecureNetworkManager.Instance.RequestPlayerInfo();
        }
        
        ShowNotification("Connected to server");
    }
    
    private void OnDisconnected()
    {
        HideConnectionPanel();
        ShowMainMenu();
        ShowNotification("Disconnected from server");
    }
    
    private void OnConnectionFailed()
    {
        HideConnectionPanel();
        ShowMainMenu();
        ShowNotification("Failed to connect to server");
    }
    
    private void OnRoomListReceived(Dictionary<string, object> message)
    {
        HideConnectionPanel();
        
        // Clear previous room list
        ClearRoomList();
        
        Debug.Log($"UIManager.OnRoomListReceived: {JsonConvert.SerializeObject(message)}");
        
        // Parse room list from message
        if (message.ContainsKey("rooms"))
        {
            try 
            {
                var roomsObj = message["rooms"];
                
                // Handle different possible types
                List<Dictionary<string, object>> roomList = null;
                
                if (roomsObj is Newtonsoft.Json.Linq.JArray jArray)
                {
                    roomList = jArray.ToObject<List<Dictionary<string, object>>>();
                }
                else if (roomsObj is List<Dictionary<string, object>> directList)
                {
                    roomList = directList;
                }
                else if (roomsObj is IEnumerable<object> enumerable)
                {
                    roomList = new List<Dictionary<string, object>>();
                    foreach (var item in enumerable)
                    {
                        if (item is Dictionary<string, object> dict)
                        {
                            roomList.Add(dict);
                        }
                    }
                }
                
                if (roomList == null || roomList.Count == 0)
                {
                    // Add a message to the UI when no rooms are available
                    GameObject emptyListText = new GameObject("EmptyListText");
                    emptyListText.transform.SetParent(roomListContent, false);
                    TextMeshProUGUI textComponent = emptyListText.AddComponent<TextMeshProUGUI>();
                    textComponent.text = "No rooms available. Create one!";
                    textComponent.fontSize = 24;
                    textComponent.alignment = TextAlignmentOptions.Center;
                    return;
                }
                
                Debug.Log($"Processing {roomList.Count} rooms");
                
                foreach (var room in roomList)
                {
                    Debug.Log($"Processing room: {JsonConvert.SerializeObject(room)}");
                    
                    // Create room list item
                    GameObject roomItem = Instantiate(roomListItemPrefab, roomListContent);
                    RoomListItem roomListItem = roomItem.GetComponent<RoomListItem>();
                    
                    if (roomListItem == null)
                    {
                        Debug.LogError("RoomListItem component not found on instantiated prefab!");
                        continue;
                    }
                    
                    try {
                        // Extract room data with correct field names from server
                        string roomId = room.ContainsKey("id") ? room["id"].ToString() : "";
                        string roomName = room.ContainsKey("name") ? room["name"].ToString() : "Unknown Room";
                        
                        // Handle playerCount numeric conversion safely
                        int playerCount = 0;
                        if (room.ContainsKey("playerCount"))
                        {
                            if (room["playerCount"] is int intVal)
                                playerCount = intVal;
                            else
                                int.TryParse(room["playerCount"].ToString(), out playerCount);
                        }
                        
                        // Handle maxPlayers numeric conversion safely  
                        int maxPlayers = 20; // Default max players
                        if (room.ContainsKey("maxPlayers"))
                        {
                            if (room["maxPlayers"] is int intVal)
                                maxPlayers = intVal;
                            else
                                int.TryParse(room["maxPlayers"].ToString(), out maxPlayers);
                        }
                        
                        // Skip empty room IDs
                        if (string.IsNullOrEmpty(roomId))
                        {
                            Debug.LogWarning("Skipping room with empty ID");
                            Destroy(roomItem);
                            continue;
                        }
                        
                        Debug.Log($"Initializing room UI: ID={roomId}, Name={roomName}, Players={playerCount}/{maxPlayers}");
                        roomListItem.Initialize(roomId, roomName, playerCount, maxPlayers);
                        roomListItem.OnSelected += OnRoomSelected;
                    }
                    catch (Exception e) {
                        Debug.LogError($"Error initializing room list item: {e.Message}\nRoom data: {JsonConvert.SerializeObject(room)}");
                        Destroy(roomItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing rooms list: {ex.Message}\n{ex.StackTrace}");
                
                // Add a friendly error message to the room list
                GameObject errorText = new GameObject("ErrorText");
                errorText.transform.SetParent(roomListContent, false);
                TextMeshProUGUI textComponent = errorText.AddComponent<TextMeshProUGUI>();
                textComponent.text = "Error loading room list. Please try again.";
                textComponent.fontSize = 20;
                textComponent.color = Color.red;
                textComponent.alignment = TextAlignmentOptions.Center;
            }
        }
        else
        {
            Debug.LogError("Room list message does not contain 'rooms' key");
            
            // Add a friendly error message
            GameObject errorText = new GameObject("ErrorText");
            errorText.transform.SetParent(roomListContent, false);
            TextMeshProUGUI textComponent = errorText.AddComponent<TextMeshProUGUI>();
            textComponent.text = "No rooms found. Try creating one!";
            textComponent.fontSize = 20;
            textComponent.alignment = TextAlignmentOptions.Center;
        }
    }
    
    private void OnRoomSelected(string roomId, string roomName)
    {
        currentRoomId = roomId;
        currentRoomName = roomName;
    }
    
    private void OnGameHosted(Dictionary<string, object> message)
    {
        Debug.Log($"OnGameHosted called with message: {JsonConvert.SerializeObject(message)}");
        
        HideConnectionPanel();
        
        if (message.ContainsKey("room_id"))
        {
            currentRoomId = message["room_id"].ToString();
            currentRoomName = createRoomNameInput.text;
            isHost = true;  // Explicitly set host status
            
            // Add self to players list
            string clientId = SecureNetworkManager.Instance.GetClientId();
            playersInRoom.Clear();
            playersInRoom.Add(clientId);
            
            Debug.Log($"Room created: ID={currentRoomId}, Name={currentRoomName}, ClientID={clientId}, isHost={isHost}");
            
            // IMPORTANT: Add explicit UI update
            ShowRoomLobbyPanel();
            UpdateRoomInfo();
            ShowNotification("Room created successfully");
        }
        else
        {
            Debug.LogError("OnGameHosted message missing room_id!");
        }
    }
    
    private void OnRoomPlayersReceived(Dictionary<string, object> message)
    {
        // Debug log commented for cleaner console
        // Debug.Log($"OnRoomPlayersReceived: {JsonConvert.SerializeObject(message)}");
        
        // Clear the current player list to get fresh data
        playersInRoom.Clear();
        
        if (message.ContainsKey("players"))
        {
            Newtonsoft.Json.Linq.JArray playersArray = null;
            
            // Handle different possible data types
            if (message["players"] is Newtonsoft.Json.Linq.JArray jArray)
            {
                playersArray = jArray;
            }
            else if (message["players"] is string jsonStr)
            {
                // Try to parse string as JSON
                try
                {
                    playersArray = Newtonsoft.Json.Linq.JArray.Parse(jsonStr);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse players JSON string: {e.Message}");
                }
            }
            
            if (playersArray != null)
            {
                Debug.Log($"Found {playersArray.Count} players in room");
                
                foreach (var playerToken in playersArray)
                {
                    try
                    {
                        // The player data can be either a simple ID string or a complete object
                        if (playerToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            string playerId = playerToken.ToString();
                            if (!string.IsNullOrEmpty(playerId))
                            {
                                playersInRoom.Add(playerId);
                                // Debug.Log($"Added player ID: {playerId}");
                            }
                        }
                        else if (playerToken.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        {
                            // Extract ID from player object
                            var playerObj = playerToken.ToObject<Dictionary<string, object>>();
                            if (playerObj != null && playerObj.ContainsKey("id"))
                            {
                                string playerId = playerObj["id"].ToString();
                                if (!string.IsNullOrEmpty(playerId))
                                {
                                    playersInRoom.Add(playerId);
                                    // Debug.Log($"Added player ID from object: {playerId}");
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error processing player token: {e.Message}");
                    }
                }
                
                // Check if we're added to the room ourselves
                string clientId = SecureNetworkManager.Instance.GetClientId();
                if (!playersInRoom.Contains(clientId))
                {
                    playersInRoom.Add(clientId);
                    Debug.Log($"Added self to player list: {clientId}");
                }
                
                // Properly update the UI
                UpdateRoomInfo();
            }
            else
            {
                Debug.LogError("Failed to parse players array");
            }
        }
        else
        {
            Debug.LogError("Room players message does not contain 'players' key");
        }
    }

    // In OnJoinedGame method - update to use SecureNetworkManager
    private void OnJoinedGame(Dictionary<string, object> message)
    {
        HideConnectionPanel();

        if (message.ContainsKey("room_id"))
        {
            currentRoomId = message["room_id"].ToString();

            // Determine if we're the host
            string hostId = "";
            string clientId = SecureNetworkManager.Instance.GetClientId();
            
            if (message.ContainsKey("host_id") && message["host_id"] != null && message["host_id"].ToString() != "unknown")
            {
                hostId = message["host_id"].ToString();
                isHost = (hostId == clientId);
            }
            else
            {
                // Fallback: if we can't determine host, assume we're not the host
                // We can update this later when we get room players information
                isHost = false;
                Debug.LogWarning("Could not determine host status from join response, assuming not host");
            }
            
            Debug.Log($"Joined room: ID={currentRoomId}, ClientID={clientId}, HostID={hostId}, isHost={isHost}");

            // Clear the player list and add ourselves
            playersInRoom.Clear();
            playersInRoom.Add(clientId);

            // Request the complete player list from the server
            if (SecureNetworkManager.Instance != null)
            {
                // Use the proper helper method
                _ = SecureNetworkManager.Instance.GetRoomPlayers(currentRoomId);
                Debug.Log($"Sent request for room players: {currentRoomId}");
            }

            ShowRoomLobbyPanel();
            UpdateRoomInfo();
            ShowNotification("Joined room successfully");
        }
        else
        {
            Debug.LogError("OnJoinedGame message missing room_id!");
        }
    }
    
    private void OnPlayerJoined(Dictionary<string, object> message)
    {
        if (message.ContainsKey("client_id"))
        {
            string playerId = message["client_id"].ToString();
            if (!playersInRoom.Contains(playerId))
            {
                playersInRoom.Add(playerId);
                UpdateRoomInfo();
                ShowNotification($"Player {playerId} joined the room");
            }
        }
    }
    
    private void OnPlayerLeftRoom(Dictionary<string, object> message)
    {
        if (message.ContainsKey("player_id"))
        {
            string playerId = message["player_id"].ToString();
            if (playersInRoom.Contains(playerId))
            {
                playersInRoom.Remove(playerId);
                UpdateRoomInfo();
                ShowNotification($"Player {playerId} left the room");
            }
        }
    }
    
    private void OnGameStarted(Dictionary<string, object> message)
    {
        HideConnectionPanel();
        
        // Show clear feedback to the user that the game is starting
        ShowNotification("Game starting! Loading race track...");
        
        Debug.Log($"OnGameStarted received with message: {JsonConvert.SerializeObject(message)}");
        
        // Check for spawn_positions (plural, snake_case) as sent by SecureNetworkManager
        if (message.ContainsKey("spawn_positions"))
        {
            var spawnPositionsObj = message["spawn_positions"] as Newtonsoft.Json.Linq.JObject;
            
            // Get the current client's ID to find their spawn position
            string clientId = SecureNetworkManager.Instance?.GetClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                Debug.LogError("Client ID is null or empty when trying to get spawn position!");
                ShowNotification("Error: Client ID not found. Please restart the game.");
                return;
            }
            
            // Look for this client's spawn position in the spawn_positions object
            if (spawnPositionsObj != null && spawnPositionsObj.ContainsKey(clientId))
            {
                var spawnPosObj = spawnPositionsObj[clientId] as Newtonsoft.Json.Linq.JObject;
                
                // Extract spawn position
                Vector3 spawnPosition = new Vector3(
                    Convert.ToSingle(spawnPosObj["x"]),
                    Convert.ToSingle(spawnPosObj["y"]),
                    Convert.ToSingle(spawnPosObj["z"])
                );
                
                // Extract spawn index if available (assign index based on player order for now)
                int spawnIndex = 0;
                if (spawnPosObj.ContainsKey("index"))
                {
                    spawnIndex = Convert.ToInt32(spawnPosObj["index"]);
                }
                
                Debug.Log($"Game started with spawn position: {spawnPosition}, index: {spawnIndex} for client: {clientId}");
                
                // Request the player list before loading the scene
                if (SecureNetworkManager.Instance != null && !string.IsNullOrEmpty(currentRoomId))
                {
                    // Use the proper helper method
                    _ = SecureNetworkManager.Instance.GetRoomPlayers(currentRoomId);
                }

                // Hide UI and ensure all panels are disabled before loading scene
                HideAllPanels();
                
                // IMPORTANT: Set spawn position in GameManager BEFORE loading scene
                if (GameManager.Instance != null)
                {
                    // Set the spawn position and index before loading the scene
                    GameManager.Instance.SetMultiplayerSpawnPosition(spawnPosition, spawnIndex);
                    
                    // Load the race track scene - use the correct scene name
                    // FIXED: Use the generic "RaceTrack" scene name instead of track-specific
                    string sceneName = "RaceTrack";
                    
                    Debug.Log($"Loading race scene: {sceneName} (immediate scene transition)");
                    
                    // Force GC collection before loading scene to reduce potential stutter
                    System.GC.Collect();
                    
                    // Use the LoadSceneAsync method with an immediate callback to ensure the scene loads
                    StartCoroutine(LoadRaceSceneAsync(sceneName));
                }
                else
                {
                    Debug.LogError("GameManager.Instance is null when trying to start game!");
                    ShowNotification("Error: Game manager not found. Please restart the game.");
                }
            }
            else
            {
                Debug.LogError($"Spawn position not found for client ID: {clientId} in spawn positions object!");
                ShowNotification("Error: Your spawn position was not assigned. Please try again.");
            }
        }
        else
        {
            Debug.LogError("Spawn position data missing in game start message!");
            ShowNotification("Error: Missing spawn data. Please try again.");
        }
    }
    
    // Add a coroutine to handle scene loading asynchronously
    private IEnumerator LoadRaceSceneAsync(string sceneName)
    {
        Debug.Log($"Starting async scene load for {sceneName}");
        
        // Show loading screen
        ShowLoadingScreen("Loading Race Track...");
        
        // Small delay to ensure loading screen is visible
        yield return new WaitForSeconds(0.1f);
        
        // Begin loading the scene
        AsyncOperation asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        
        // Show loading progress
        while (!asyncLoad.isDone)
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            UpdateLoadingProgress(progress, $"Loading Race Track... {(progress * 100):0}%");
            
            yield return null;
        }
        
        // Final progress update
        UpdateLoadingProgress(1f, "Loading Complete!");
        yield return new WaitForSeconds(0.5f); // Brief pause to show completion
        
        // Hide loading screen
        HideLoadingScreen();
        
        Debug.Log("Scene load completed");
    }    
    private void OnServerMessage(Dictionary<string, object> message)
    {
        if (message.ContainsKey("message"))
        {
            string serverMessage = message["message"].ToString();
            ShowNotification($"Server: {serverMessage}");
        }
        
        // Handle AUTH_FAILED messages
        if (message.ContainsKey("command") && message["command"].ToString() == "AUTH_FAILED")
        {
            string errorMessage = "Authentication failed";
            if (message.ContainsKey("message"))
            {
                errorMessage = message["message"].ToString();
            }
            
            HideConnectionPanel();
            ShowAuthPanel(errorMessage);
        }
    }
    
    #endregion

    // Add a periodic refresh of the player list to catch any updates
    private float lastPlayerListRefreshTime = 0f;
    private const float PLAYER_LIST_REFRESH_INTERVAL = 3f; // Refresh player list every 3 seconds

    [Header("FPS Counter")]
    public TextMeshProUGUI fpsText;
    public float fpsUpdateInterval = 0.5f; // How often to update the FPS display
    public int fpsAverageSamples = 20; // How many frames to average
    private float[] fpsSamples;
    private int fpsSampleIndex = 0;
    private float fpsAccumulator = 0f;
    private float fpsNextUpdateTime = 0f;
    private int fpsFrameCount = 0;

    private void Update()
    {
        // Periodically refresh the player list when in room lobby
        if (roomLobbyPanel != null && roomLobbyPanel.activeSelf && !string.IsNullOrEmpty(currentRoomId))
        {
            if (Time.time - lastPlayerListRefreshTime > PLAYER_LIST_REFRESH_INTERVAL)
            {
                RefreshPlayerList();
                lastPlayerListRefreshTime = Time.time;
            }
        }
        
        // Update network latency display
        if (latencyText != null && SecureNetworkManager.Instance != null && Time.time - lastLatencyUpdateTime > LATENCY_UPDATE_INTERVAL)
        {
            float latency = SecureNetworkManager.Instance.GetLatency();
            latencyText.text = $"Ping: {(latency * 1000):0}ms";
            latencyText.color = latency < 0.1f ? Color.green : (latency < 0.3f ? Color.yellow : Color.red);
            lastLatencyUpdateTime = Time.time;
        }
        
        // Check if we need to find the player car for UI updates
        if (IsRaceScene() && !carUIInitialized)
        {
            // Try to find player car every frame until we connect
            FindPlayerCarController();
        }

        // FPS Counter update
        UpdateFPSCounter();
    }

    private void RefreshPlayerList()
    {
        // Request the complete player list from the server
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected() && !string.IsNullOrEmpty(currentRoomId))
        {
            // Use the proper helper method that formats the command correctly
            _ = SecureNetworkManager.Instance.GetRoomPlayers(currentRoomId);
            Debug.Log("Sending periodic player list refresh request");
        }
    }

    // Add a method to check if the scene is already loaded to prevent duplicate loads
    public bool IsSceneLoaded(string sceneName)
    {
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (scene.name == sceneName)
            {
                return true;
            }
        }
        return false;
    }

    // Find and connect to the player car controller when it spawns
    private void FindPlayerCarController()
    {
        // Only search for car controller in race scenes
        if (IsRaceScene())
        {
            GameObject playerCar = GameObject.FindGameObjectWithTag("Player");
            if (playerCar != null)
            {
                playerCarController = playerCar.GetComponent<CarController>();
                if (playerCarController != null)
                {
                    // Subscribe to the car stats update event
                    playerCarController.OnCarStatsUpdated += UpdateCarUI;
                    carUIInitialized = true;
                    Debug.Log("Connected to player car for UI updates");
                }
            }
        }
    }
    
    // Check if we're in a race scene
    private bool IsRaceScene()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        return currentScene.Contains("Track") || currentScene.Contains("Race");
    }
    
    // Update car UI elements with latest car stats
    private void UpdateCarUI(float speed, float rpm, int gear)
    {
        // Update speed display
        if (speedText != null)
        {
            float speedValue = speed;
            if (!showKMH)
            {
                speedValue = speedValue * 0.6213712f; // Convert to MPH
            }
            speedText.text = speedValue.ToString(speedFormat) + (showKMH ? " km/h" : " mph");
        }

        // Update RPM display
        if (rpmText != null)
        {
            rpmText.text = rpm.ToString(rpmFormat) + " RPM";
        }

        // Update gear display if enabled
        if (gearText != null && showGear)
        {
            string gearDisplay;
            if (gear > 0)
            {
                gearDisplay = gear.ToString();
            }
            else if (gear == 0)
            {
                gearDisplay = "N";
            }
            else
            {
                gearDisplay = "R";
            }
            gearText.text = gearDisplay;
        }
    }

    // Method to handle scene changes
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool isRaceScene = IsRaceScene();
        
        // Show/hide race UI based on scene
        if (raceUIPanel != null)
        {
            raceUIPanel.SetActive(isRaceScene);
            isRaceUIVisible = isRaceScene;
        }
        
        // Show/hide network stats panel based on scene
        if (networkStatsPanel != null)
        {
            networkStatsPanel.SetActive(isRaceScene);
        }
        
        // Reset car UI connection when entering race scene
        if (isRaceScene)
        {
            carUIInitialized = false;
        }
    }

    private void UpdateFPSCounter()
    {
        if (fpsText == null) return;

        // Initialize FPS samples array if not already done
        if (fpsSamples == null || fpsSamples.Length != fpsAverageSamples)
        {
            fpsSamples = new float[fpsAverageSamples];
            for (int i = 0; i < fpsSamples.Length; i++)
            {
                fpsSamples[i] = 0;
            }
        }

        // Accumulate frame time
        fpsAccumulator += Time.unscaledDeltaTime;
        fpsFrameCount++;

        // Check if it's time to update the FPS display
        if (Time.unscaledTime > fpsNextUpdateTime)
        {
            // Calculate average FPS
            float averageFrameTime = fpsAccumulator / fpsFrameCount;
            float fps = 1.0f / averageFrameTime;

            // Store the FPS sample
            fpsSamples[fpsSampleIndex] = fps;
            fpsSampleIndex = (fpsSampleIndex + 1) % fpsAverageSamples;

            // Calculate the average FPS over the samples
            float averageFPS = 0f;
            int validSamples = 0;
            foreach (float sample in fpsSamples)
            {
                if (sample > 0)
                {
                    averageFPS += sample;
                    validSamples++;
                }
            }
            averageFPS = validSamples > 0 ? averageFPS / validSamples : 0;

            // Update the FPS display with color coding
            string fpsText = $"FPS: {averageFPS:F1}";
            
            // Color code based on performance
            Color textColor = Color.green;
            if (averageFPS < 30)
                textColor = Color.red;
            else if (averageFPS < 60)
                textColor = Color.yellow;
                
            this.fpsText.text = fpsText;
            this.fpsText.color = textColor;

            // Reset the accumulator and frame count
            fpsAccumulator = 0f;
            fpsFrameCount = 0;

            // Set the next update time
            fpsNextUpdateTime = Time.unscaledTime + fpsUpdateInterval;
        }
    }
}