using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.IO;

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
    
    [Header("Profile Management UI")]
    public GameObject registerPanel;
    public TMP_InputField registerUsernameInput;
    public TMP_InputField registerPasswordInput;
    public TMP_InputField registerConfirmPasswordInput;
    public Button registerButton;
    public Button backFromRegisterButton;
    public TextMeshProUGUI registerStatusText;
    
    [Header("Login UI")]
    public GameObject loginPanel;
    public TMP_InputField loginUsernameInput;
    public TMP_InputField loginPasswordInput;
    public Button loginSubmitButton;
    public Button backFromLoginButton;
    public TextMeshProUGUI loginStatusText;
    
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
    public Button deleteProfileButton;
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

    [Header("Confirmation Dialog")]
    public GameObject confirmationPanel;
    public TextMeshProUGUI confirmationText;
    public Button confirmYesButton;
    public Button confirmNoButton;
    
    // Profile management state
    private CarController playerCarController;
    private bool carUIInitialized = false;
    
    // Profile management state
    private ProfileData selectedProfile;
    private bool isFirstTimeRegistration = false;
    
    // Player profile data
    private string playerName = "Player";
    private string playerId;
    private List<ProfileData> savedProfiles = new List<ProfileData>();
    
    // Room management - Updated for MP-Server protocol
    private List<RoomInfo> roomList = new List<RoomInfo>();
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
        public string password; // Store encrypted password for local profile management
        public bool hasPassword; // Flag to indicate if password is set
        
        public ProfileData(string name, string id)
        {
            this.name = name;
            this.id = id;
            this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            this.password = "";
            this.hasPassword = false;
        }
        
        public ProfileData(string name, string id, string password)
        {
            this.name = name;
            this.id = id;
            this.lastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            this.password = password;
            this.hasPassword = !string.IsNullOrEmpty(password);
        }
        
        public void SetPassword(string encryptedPassword)
        {
            this.password = encryptedPassword;
            this.hasPassword = !string.IsNullOrEmpty(encryptedPassword);
        }
        
        public void UpdateLastPlayed()
        {
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
        
        // Register for network events - MP-Server protocol events
        SecureNetworkManager networkManager = SecureNetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected += OnConnected;
            networkManager.OnDisconnected += OnDisconnected;
            networkManager.OnConnectionFailed += OnConnectionFailed;
            networkManager.OnAuthenticationChanged += OnAuthenticationChanged;
            networkManager.OnRoomCreated += OnRoomCreated;
            networkManager.OnRoomJoined += OnRoomJoined;
            networkManager.OnRoomListReceived += OnRoomListReceived;
            networkManager.OnGameStarted += OnGameStarted;
            networkManager.OnPlayerPositionUpdate += OnPlayerPositionUpdate;
            networkManager.OnPlayerInputUpdate += OnPlayerInputUpdate;
            networkManager.OnMessageReceived += OnMessageReceived;
            networkManager.OnError += OnNetworkError;
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
        ConnectButtonDirect(exitButton, Application.Quit, "ExitButton");
        
        // Profile panel buttons
        ConnectButtonDirect(createProfileButton, CreateNewProfile, "CreateProfileButton");
        ConnectButtonDirect(backToMainButton, ShowMainMenu, "BackToMainButton");
        
        // Instructions panel buttons
        ConnectButtonDirect(backFromInstructionsButton, BackFromInstructions, "BackFromInstructionsButton");
        
        // Credits panel buttons
        ConnectButtonDirect(backFromCreditsButton, BackFromCredits, "BackFromCreditsButton");
        
        // Profile panel buttons (additional)
        ConnectButtonDirect(backFromProfileButton, BackFromProfile, "BackFromProfileButton");
        ConnectButtonDirect(deleteProfileButton, DeleteSelectedProfile, "DeleteProfileButton");
        
        // Registration panel buttons
        ConnectButtonDirect(registerButton, OnRegisterButtonClicked, "RegisterButton");
        ConnectButtonDirect(backFromRegisterButton, BackFromRegister, "BackFromRegisterButton");
        
        // Login panel buttons
        ConnectButtonDirect(loginSubmitButton, OnLoginSubmitButtonClicked, "LoginSubmitButton");
        ConnectButtonDirect(backFromLoginButton, BackFromLogin, "BackFromLoginButton");
        ConnectButtonDirect(deleteProfileButton, DeleteSelectedProfile, "DeleteProfileButton");
        
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
    
    // In OnDestroy method - unsubscribe from MP-Server events
    void OnDestroy()
    {
        // Unregister network events
        SecureNetworkManager networkManager = SecureNetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected -= OnConnected;
            networkManager.OnDisconnected -= OnDisconnected;
            networkManager.OnConnectionFailed -= OnConnectionFailed;
            networkManager.OnAuthenticationChanged -= OnAuthenticationChanged;
            networkManager.OnRoomCreated -= OnRoomCreated;
            networkManager.OnRoomJoined -= OnRoomJoined;
            networkManager.OnRoomListReceived -= OnRoomListReceived;
            networkManager.OnGameStarted -= OnGameStarted;
            networkManager.OnPlayerPositionUpdate -= OnPlayerPositionUpdate;
            networkManager.OnPlayerInputUpdate -= OnPlayerInputUpdate;
            networkManager.OnMessageReceived -= OnMessageReceived;
            networkManager.OnError -= OnNetworkError;
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
        if (SecureNetworkManager.Instance != null && !SecureNetworkManager.Instance.IsConnected)
        {
            ShowConnectionPanel("Connecting to server...");
            _ = SecureNetworkManager.Instance.ConnectToServerAsync();
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
    
    // Profile exists, check if we need to authenticate
    if (SecureNetworkManager.Instance == null || !SecureNetworkManager.Instance.IsAuthenticated)
    {
        // Find the current profile and show appropriate auth screen
        var currentProfile = savedProfiles.Find(p => p.id == playerId);
        if (currentProfile != null)
        {
            SelectProfileForAuthentication(currentProfile);
        }
        else
        {
            // Profile data is inconsistent, show profile selection
            ShowProfilePanel();
            ShowNotification("Please select a profile to continue");
        }
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
        
        // Hide registration panel if it exists
        if (registerPanel != null)
        {
            registerPanel.SetActive(false);
        }
        
        // Hide login panel if it exists
        if (loginPanel != null)
        {
            loginPanel.SetActive(false);
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
        
        // Hide confirmation panel if it exists
        if (confirmationPanel != null)
        {
            confirmationPanel.SetActive(false);
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
            
            // Update local player name
            playerName = username;
            
            // Show connection panel
            ShowConnectionPanel("Authenticating...");
            
            // If not connected, connect
            if (!SecureNetworkManager.Instance.IsConnected)
            {
                _ = SecureNetworkManager.Instance.ConnectToServerAsync();
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
        
        // Check if name already exists
        var existingProfile = savedProfiles.Find(p => p.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingProfile != null)
        {
            ShowNotification("Profile name already exists. Please choose a different name.");
            return;
        }
        
        // Generate a player ID
        string id = GenerateUniquePlayerId(name);
        
        // Create new profile without password (will be set during registration)
        ProfileData profile = new ProfileData(name, id);
        savedProfiles.Add(profile);
        
        // Save to disk
        SaveProfiles();
        
        // Set as selected profile and show registration
        selectedProfile = profile;
        isFirstTimeRegistration = true;
        ShowRegisterPanel();
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
        if (currentProfileText != null)
        {
            currentProfileText.text = $"Profile: {playerName}";
        }
        
        // Update profile's last played time
        profile.UpdateLastPlayed();
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
    
    // Show message if no profiles exist
    if (savedProfiles.Count == 0)
    {
        GameObject noProfilesText = new GameObject("NoProfilesText");
        noProfilesText.transform.SetParent(profileListContent, false);
        TextMeshProUGUI textComponent = noProfilesText.AddComponent<TextMeshProUGUI>();
        textComponent.text = "No profiles found. Create your first profile!";
        textComponent.fontSize = 18;
        textComponent.alignment = TextAlignmentOptions.Center;
        textComponent.color = Color.gray;
        return;
    }
    
    // Add profiles
    foreach (ProfileData profile in savedProfiles)
    {
        GameObject profileItem = Instantiate(profileListItemPrefab, profileListContent);
        
        // Try to use ProfileListItem component first
        ProfileListItem profileListItem = profileItem.GetComponent<ProfileListItem>();
        if (profileListItem != null)
        {
            // Use the enhanced ProfileListItem component
            profileListItem.Initialize(profile);
            profileListItem.OnProfileSelected += SelectProfileForAuthentication;
            profileListItem.OnProfileDeleted += DeleteProfile;
        }
        else
        {
            // Fallback to manual setup for backward compatibility
            SetupProfileItemManually(profileItem, profile);
        }
    }
}

private void SetupProfileItemManually(GameObject profileItem, ProfileData profile)
{
    // Check for required child objects
    Transform nameTextTransform = profileItem.transform.Find("NameText");
    if (nameTextTransform != null)
    {
        TextMeshProUGUI nameText = nameTextTransform.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = profile.name;
        }
    }
    
    Transform infoTextTransform = profileItem.transform.Find("InfoText");
    if (infoTextTransform != null)
    {
        TextMeshProUGUI infoText = infoTextTransform.GetComponent<TextMeshProUGUI>();
        if (infoText != null)
        {
            string statusText = profile.hasPassword ? "Ready to play" : "Setup required";
            infoText.text = $"Last played: {profile.lastPlayed} • {statusText}";
        }
    }
    
    // Set button callback for selection
    Button selectButton = profileItem.GetComponent<Button>();
    if (selectButton != null)
    {
        ProfileData profileCopy = profile; // Create a copy for the closure
        selectButton.onClick.AddListener(() => {
            selectedProfile = profileCopy;
            SelectProfileForAuthentication(profileCopy);
        });
    }
    
    // Look for delete button in the profile item
    Transform deleteButtonTransform = profileItem.transform.Find("DeleteButton");
    if (deleteButtonTransform != null)
    {
        Button deleteButton = deleteButtonTransform.GetComponent<Button>();
        if (deleteButton != null)
        {
            ProfileData profileCopy = profile; // Create a copy for the closure
            deleteButton.onClick.AddListener(() => {
                DeleteProfile(profileCopy);
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
    
    public void DeleteSelectedProfile()
    {
        if (selectedProfile == null)
        {
            ShowNotification("No profile selected to delete");
            return;
        }
        
        DeleteProfile(selectedProfile);
    }
    
    #endregion
    
    #region Room Management - MP-Server Protocol
    
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
        if (!SecureNetworkManager.Instance.IsAuthenticated)
        {
            ShowNotification("Please log in before creating a room");
            ShowAuthPanel();
            return;
        }
        
        // Show connection panel immediately to provide feedback
        ShowConnectionPanel("Creating room...");
        
        // Make sure we're connected before attempting to create a room
        if (!SecureNetworkManager.Instance.IsConnected)
        {
            ShowConnectionPanel("Connecting to server...");
            
            try 
            {
                await SecureNetworkManager.Instance.ConnectToServerAsync();
                
                // Check if we're connected after the await
                if (!SecureNetworkManager.Instance.IsConnected) 
                {
                    ShowNotification("Could not connect to server");
                    HideConnectionPanel();
                    return;
                }
            }
            catch (Exception e) 
            {
                ShowNotification("Connection error: " + e.Message);
                HideConnectionPanel();
                return;
            }
        }
        
        string roomName = createRoomNameInput.text;
        if (string.IsNullOrEmpty(roomName))
            roomName = $"{playerName}'s Room";
        
        try 
        {
            await SecureNetworkManager.Instance.CreateRoomAsync(roomName);
            // The actual feedback will come through the OnRoomCreated event callback
        }
        catch (Exception e) 
        {
            ShowNotification("Error creating room: " + e.Message);
            HideConnectionPanel();
        }
    }
    
    // Join selected room using MP-Server protocol
    public async void JoinSelectedRoom()
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            ShowNotification("Please select a room to join");
            return;
        }
        
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected)
        {
            ShowConnectionPanel("Joining room...");
            try
            {
                await SecureNetworkManager.Instance.JoinRoomAsync(currentRoomId);
                // The actual feedback will come through the OnRoomJoined event callback
            }
            catch (Exception e)
            {
                ShowNotification("Error joining room: " + e.Message);
                HideConnectionPanel();
            }
        }
        else
        {
            ShowNotification("Not connected to server");
        }
    }
    
    // Start game using MP-Server protocol
    public async void StartGame()
    {
        if (!isHost)
        {
            ShowNotification("Only the host can start the game");
            return;
        }
        
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected && !string.IsNullOrEmpty(currentRoomId))
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
            
            try
            {
                await SecureNetworkManager.Instance.StartGameAsync();
                // The actual feedback will come through the OnGameStarted event callback
            }
            catch (Exception e)
            {
                ShowNotification("Error starting game: " + e.Message);
                HideConnectionPanel();
            }
        }
        else
        {
            ShowNotification("Cannot start game - connection issue");
        }
    }
    
    public async void LeaveRoom()
    {
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected)
        {
            try
            {
                await SecureNetworkManager.Instance.LeaveRoomAsync();
                ShowRoomListPanel();
                
                // Reset room state
                currentRoomId = null;
                currentRoomName = null;
                isHost = false;
                playersInRoom.Clear();
            }
            catch (Exception e)
            {
                ShowNotification("Error leaving room: " + e.Message);
            }
        }
    }
    
    // Refresh room list using MP-Server protocol
    public async void RefreshRoomList()
    {
        if (SecureNetworkManager.Instance == null)
        {
            ShowNotification("Network manager not available");
            return;
        }
        
        // Check if we're connected to the server
        if (!SecureNetworkManager.Instance.IsConnected)
        {
            ShowConnectionPanel("Connecting to server...");
            
            try 
            {
                await SecureNetworkManager.Instance.ConnectToServerAsync();
                
                // Check connection status after awaiting
                if (!SecureNetworkManager.Instance.IsConnected)
                {
                    ShowNotification("Failed to connect to server");
                    HideConnectionPanel();
                    return;
                }
            }
            catch (Exception e) 
            {
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
        try
        {
            await SecureNetworkManager.Instance.RequestRoomListAsync();
        }
        catch (Exception e)
        {
            ShowNotification("Error requesting room list: " + e.Message);
            ClearRoomList();
        }
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
        if (roomInfoText == null || playerCountText == null || startGameButton == null || 
            playerListContent == null || playerListItemPrefab == null)
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
        string sessionId = SecureNetworkManager.Instance?.SessionId;
        foreach (string playerId in playersInRoom)
        {
            GameObject playerItem = Instantiate(playerListItemPrefab, playerListContent);
            TextMeshProUGUI playerText = playerItem.GetComponentInChildren<TextMeshProUGUI>();
    
            if (playerText == null)
            {
                continue;
            }            
            
            string playerDisplayName = playerId;
            if (!string.IsNullOrEmpty(sessionId) && playerId == sessionId)
                playerDisplayName += " (You)";

            playerText.text = playerDisplayName;
        }
    }
    
    #endregion
    
    #region Network Event Handlers - MP-Server Protocol
    
    private void OnConnected(string message)
    {
        HideConnectionPanel();
        ShowNotification($"Connected: {message}");
    }
    
    private void OnDisconnected(string message)
    {
        HideConnectionPanel();
        ShowMainMenu();
        ShowNotification($"Disconnected: {message}");
    }
    
    private void OnConnectionFailed(string message)
    {
        HideConnectionPanel();
        ShowMainMenu();
        ShowNotification($"Connection failed: {message}");
    }
    
    private void OnAuthenticationChanged(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            HideConnectionPanel();
            ShowNotification("Authentication successful");
        }
        else
        {
            ShowAuthPanel("Authentication failed. Please try again.");
        }
    }
    
    private void OnRoomCreated(RoomInfo roomInfo)
    {
        HideConnectionPanel();
        
        currentRoomId = roomInfo.Id;
        currentRoomName = roomInfo.Name;
        isHost = true; // Creator is always host
        
        // Add self to players list
        string sessionId = SecureNetworkManager.Instance?.SessionId;
        playersInRoom.Clear();
        if (!string.IsNullOrEmpty(sessionId))
        {
            playersInRoom.Add(sessionId);
        }
        
        Debug.Log($"Room created: ID={currentRoomId}, Name={currentRoomName}, SessionID={sessionId}, isHost={isHost}");
        
        ShowRoomLobbyPanel();
        UpdateRoomInfo();
        ShowNotification("Room created successfully");
    }
    
    private void OnRoomJoined(RoomInfo roomInfo)
    {
        HideConnectionPanel();
        
        currentRoomId = roomInfo.Id;
        currentRoomName = roomInfo.Name ?? "Unknown Room";
        
        // Determine if we're the host
        string sessionId = SecureNetworkManager.Instance?.SessionId;
        isHost = (roomInfo.HostId == sessionId);
        
        // Add self to players list
        playersInRoom.Clear();
        if (!string.IsNullOrEmpty(sessionId))
        {
            playersInRoom.Add(sessionId);
        }
        
        Debug.Log($"Joined room: ID={currentRoomId}, SessionID={sessionId}, HostID={roomInfo.HostId}, isHost={isHost}");
        
        ShowRoomLobbyPanel();
        UpdateRoomInfo();
        ShowNotification("Joined room successfully");
    }
    
    private void OnRoomListReceived(List<RoomInfo> rooms)
    {
        HideConnectionPanel();
        
        // Clear previous room list
        ClearRoomList();
        
        roomList = rooms ?? new List<RoomInfo>();
        
        Debug.Log($"Received room list with {roomList.Count} rooms");
        
        if (roomList.Count == 0)
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
        
        foreach (var room in roomList)
        {
            Debug.Log($"Processing room: ID={room.Id}, Name={room.Name}, Players={room.PlayerCount}, Active={room.IsActive}");
            
            // Create room list item
            GameObject roomItem = Instantiate(roomListItemPrefab, roomListContent);
            RoomListItem roomListItem = roomItem.GetComponent<RoomListItem>();
            
            if (roomListItem == null)
            {
                Debug.LogError("RoomListItem component not found on instantiated prefab!");
                Destroy(roomItem);
                continue;
            }
            
            try
            {
                // Skip empty room IDs
                if (string.IsNullOrEmpty(room.Id))
                {
                    Debug.LogWarning("Skipping room with empty ID");
                    Destroy(roomItem);
                    continue;
                }
                
                Debug.Log($"Initializing room UI: ID={room.Id}, Name={room.Name}, Players={room.PlayerCount}");
                roomListItem.Initialize(room.Id, room.Name, room.PlayerCount, 20); // Default max players
                roomListItem.OnSelected += OnRoomSelected;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error initializing room list item: {e.Message}\nRoom data: {JsonUtility.ToJson(room)}");
                Destroy(roomItem);
            }
        }
    }
    
    private void OnGameStarted(GameStartData gameData)
    {
        HideConnectionPanel();
        
        // Show clear feedback to the user that the game is starting
        ShowNotification("Game starting! Loading race track...");
        
        Debug.Log($"OnGameStarted received with RoomId: {gameData.RoomId}");
        
        // Check for spawn positions
        if (gameData.SpawnPositions != null && gameData.SpawnPositions.Count > 0)
        {
            // Get the current client's session ID to find their spawn position
            string sessionId = SecureNetworkManager.Instance?.SessionId;
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("Session ID is null or empty when trying to get spawn position!");
                ShowNotification("Error: Session ID not found. Please restart the game.");
                return;
            }
            
            // Look for this client's spawn position
            if (gameData.SpawnPositions.TryGetValue(sessionId, out Vector3 spawnPosition))
            {
                Debug.Log($"Found spawn position for session {sessionId}: {spawnPosition}");
                
                // Hide UI and ensure all panels are disabled before loading scene
                HideAllPanels();
                
                // Set spawn position in GameManager BEFORE loading scene
                if (GameManager.Instance != null)
                {
                    // Set the spawn position and index before loading the scene
                    int spawnIndex = new List<string>(gameData.SpawnPositions.Keys).IndexOf(sessionId);
                    GameManager.Instance.SetMultiplayerSpawnPosition(spawnPosition, spawnIndex);
                    
                    // Load the race track scene
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
                Debug.LogError($"Spawn position not found for session ID: {sessionId} in spawn positions!");
                ShowNotification("Error: Your spawn position was not assigned. Please try again.");
            }
        }
        else
        {
            Debug.LogError("Spawn position data missing in game start message!");
            ShowNotification("Error: Missing spawn data. Please try again.");
        }
    }
    
    private void OnPlayerPositionUpdate(PlayerUpdate playerUpdate)
    {
        // Handle position updates from other players
        // This can be forwarded to GameManager if needed
        if (GameManager.Instance != null)
        {
            // Convert PlayerUpdate to format expected by GameManager
            var playerData = new Dictionary<string, object>
            {
                ["sessionId"] = playerUpdate.SessionId,
                ["position"] = new Dictionary<string, float>
                {
                    ["x"] = playerUpdate.Position.x,
                    ["y"] = playerUpdate.Position.y,
                    ["z"] = playerUpdate.Position.z
                },
                ["rotation"] = new Dictionary<string, float>
                {
                    ["x"] = playerUpdate.Rotation.x,
                    ["y"] = playerUpdate.Rotation.y,
                    ["z"] = playerUpdate.Rotation.z,
                    ["w"] = playerUpdate.Rotation.w
                },
                ["timestamp"] = playerUpdate.Timestamp
            };
            
            // Forward to GameManager (if it has a method to handle this)
            // GameManager.Instance.HandlePlayerUpdate(playerData);
        }
    }
    
    private void OnPlayerInputUpdate(PlayerInput playerInput)
    {
        // Handle input updates from other players
        // This can be forwarded to GameManager if needed
        if (GameManager.Instance != null)
        {
            var inputData = new Dictionary<string, object>
            {
                ["sessionId"] = playerInput.SessionId,
                ["steering"] = playerInput.Steering,
                ["throttle"] = playerInput.Throttle,
                ["brake"] = playerInput.Brake,
                ["timestamp"] = playerInput.Timestamp
            };
            
            // Forward to GameManager (if it has a method to handle this)
            // GameManager.Instance.HandlePlayerInput(inputData);
        }
    }
    
    private void OnMessageReceived(RelayMessage message)
    {
        ShowNotification($"{message.SenderName}: {message.Message}");
        Debug.Log($"Message from {message.SenderName} ({message.SenderId}): {message.Message}");
    }
    
    private void OnNetworkError(string error)
    {
        ShowNotification($"Network error: {error}");
        Debug.LogError($"Network error: {error}");
    }
    
    private void OnRoomListReceived(Dictionary<string, object> message)
    {
        HideConnectionPanel();
        
        // Clear previous room list
        ClearRoomList();
        
        Debug.Log($"UIManager.OnRoomListReceived: {JsonUtility.ToJson(message)}");
        
        // Parse room list from message
        if (message.ContainsKey("rooms"))
        {
            try 
            {
                var roomsObj = message["rooms"];
                
                // Handle different possible types
                List<Dictionary<string, object>> roomList = null;
                
                if (roomsObj is List<object> objectList)
                {
                    roomList = new List<Dictionary<string, object>>();
                    foreach (var item in objectList)
                    {
                        if (item is Dictionary<string, object> dict)
                        {
                            roomList.Add(dict);
                        }
                    }
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
                    Debug.Log($"Processing room: {JsonUtility.ToJson(room)}");
                    
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
                        Debug.LogError($"Error initializing room list item: {e.Message}\nRoom data: {JsonUtility.ToJson(room)}");
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
        Debug.Log($"OnGameHosted called with message: {JsonUtility.ToJson(message)}");
        
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
        // Debug.Log($"OnRoomPlayersReceived: {JsonUtility.ToJson(message)}");
        
        // Clear the current player list to get fresh data
        playersInRoom.Clear();
        
        if (message.ContainsKey("players"))
        {
            List<object> playersArray = null;
            
            // Handle different possible data types
            if (message["players"] is List<object> playersList)
            {
                playersArray = playersList;
            }
            else if (message["players"] is string jsonStr)
            {
                // Simple JSON parsing for player arrays
                // For now, we'll handle this as a basic case
                Debug.LogWarning("String-based player arrays not fully supported with Unity JsonUtility");
                playersArray = new List<object>();
            }
            
            if (playersArray != null)
            {
                Debug.Log($"Found {playersArray.Count} players in room");
                
                foreach (var playerToken in playersArray)
                {
                    try
                    {
                        // The player data can be either a simple ID string or a complete object
                        if (playerToken is string playerId)
                        {
                            if (!string.IsNullOrEmpty(playerId))
                            {
                                playersInRoom.Add(playerId);
                                // Debug.Log($"Added player ID: {playerId}");
                            }
                        }
                        else if (playerToken is Dictionary<string, object> playerObj)
                        {
                            // Extract ID from player object
                            if (playerObj.ContainsKey("id"))
                            {
                                string playerIdFromObj = playerObj["id"].ToString();
                                if (!string.IsNullOrEmpty(playerIdFromObj))
                                {
                                    playersInRoom.Add(playerIdFromObj);
                                    // Debug.Log($"Added player ID from object: {playerIdFromObj}");
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
        
        Debug.Log($"OnGameStarted received with message: {JsonUtility.ToJson(message)}");
        
        // Check for spawn_positions (plural, snake_case) as sent by SecureNetworkManager
        if (message.ContainsKey("spawn_positions"))
        {
            var spawnPositionsObj = message["spawn_positions"] as Dictionary<string, object>;
            
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
                var spawnPosObj = spawnPositionsObj[clientId] as Dictionary<string, object>;
                
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
            float latency = SecureNetworkManager.Instance.Latency;
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
        if (SecureNetworkManager.Instance != null && SecureNetworkManager.Instance.IsConnected && !string.IsNullOrEmpty(currentRoomId))
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

    // New method to handle profile selection for authentication
    private void SelectProfileForAuthentication(ProfileData profile)
    {
        selectedProfile = profile;
        
        // If this is a first-time profile (no password saved), show registration
        if (!profile.hasPassword || string.IsNullOrEmpty(profile.password))
        {
            isFirstTimeRegistration = true;
            ShowRegisterPanel();
        }
        else
        {
            // Existing profile, show login screen
            isFirstTimeRegistration = false;
            ShowLoginPanel(profile.name);
        }
    }
    
    public void ShowRegisterPanel()
    {
        if (registerPanel == null)
        {
            ShowNotification("Registration panel not available");
            return;
        }
        
        HideAllPanels();
        registerPanel.SetActive(true);
        
        // Pre-fill username if we have a selected profile
        if (registerUsernameInput != null && selectedProfile != null)
        {
            registerUsernameInput.text = selectedProfile.name;
            registerUsernameInput.interactable = !isFirstTimeRegistration; // Lock username for existing profiles
        }
        
        if (registerStatusText != null)
        {
            registerStatusText.text = isFirstTimeRegistration ? 
                "Create your password for this profile" : 
                "Set up your account";
        }
        
        // Clear password fields
        if (registerPasswordInput != null) registerPasswordInput.text = "";
        if (registerConfirmPasswordInput != null) registerConfirmPasswordInput.text = "";
    }
    
    public void ShowLoginPanel(string username = "")
    {
        if (loginPanel == null)
        {
            ShowNotification("Login panel not available");
            return;
        }
        
        HideAllPanels();
        loginPanel.SetActive(true);
        
        // Pre-fill username
        if (loginUsernameInput != null)
        {
            loginUsernameInput.text = username;
            loginUsernameInput.interactable = false; // Lock username since we're logging into a specific profile
        }
        
        if (loginStatusText != null)
        {
            loginStatusText.text = $"Enter password for {username}";
        }
        
        // Clear password field
        if (loginPasswordInput != null) 
        {
            loginPasswordInput.text = "";
            loginPasswordInput.Select(); // Focus on password field
        }
    }
    
    public void OnRegisterButtonClicked()
    {
        string username = registerUsernameInput.text;
        string password = registerPasswordInput.text;
        string confirmPassword = registerConfirmPasswordInput.text;
        
        // Validate inputs
        if (string.IsNullOrEmpty(username))
        {
            ShowNotification("Please enter a username");
            UpdateRegisterStatus("Username is required", Color.red);
            return;
        }
        
        if (username.Length < 3)
        {
            ShowNotification("Username must be at least 3 characters long");
            UpdateRegisterStatus("Username too short", Color.red);
            return;
        }
        
        if (string.IsNullOrEmpty(password))
        {
            ShowNotification("Please enter a password");
            UpdateRegisterStatus("Password is required", Color.red);
            return;
        }
        
        if (password.Length < 4)
        {
            ShowNotification("Password must be at least 4 characters long");
            UpdateRegisterStatus("Password too short", Color.red);
            return;
        }
        
        if (password != confirmPassword)
        {
            ShowNotification("Passwords do not match");
            UpdateRegisterStatus("Passwords do not match", Color.red);
            return;
        }
        
        // Check if username already exists (if this is a new registration)
        if (!isFirstTimeRegistration)
        {
            var existingProfile = savedProfiles.Find(p => p.name.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existingProfile != null)
            {
                ShowNotification("Username already exists. Please choose a different name.");
                UpdateRegisterStatus("Username already exists", Color.red);
                return;
            }
        }
        
        UpdateRegisterStatus("Creating profile...", Color.yellow);
        
        // Create or update profile
        ProfileData profile;
        if (isFirstTimeRegistration && selectedProfile != null)
        {
            // Update existing profile with password
            profile = selectedProfile;
            profile.SetPassword(EncryptPassword(password));
        }
        else
        {
            // Create new profile
            string id = GenerateUniquePlayerId(username);
            profile = new ProfileData(username, id, EncryptPassword(password));
            savedProfiles.Add(profile);
        }
        
        // Save profiles
        SaveProfiles();
        
        UpdateRegisterStatus("Profile created successfully!", Color.green);
        
        // Set as current profile and authenticate
        SelectProfile(profile);
        
        // Small delay for user feedback, then authenticate
        StartCoroutine(AuthenticateAfterDelay(username, password, 1f));
    }
    
    private IEnumerator AuthenticateAfterDelay(string username, string password, float delay)
    {
        yield return new WaitForSeconds(delay);
        AuthenticateWithServer(username, password);
    }
    
    private void UpdateRegisterStatus(string message, Color color)
    {
        if (registerStatusText != null)
        {
            registerStatusText.text = message;
            registerStatusText.color = color;
        }
    }
    
    public void OnLoginSubmitButtonClicked()
    {
        string username = loginUsernameInput.text;
        string password = loginPasswordInput.text;
        
        if (string.IsNullOrEmpty(password))
        {
            ShowNotification("Please enter your password");
            UpdateLoginStatus("Password is required", Color.red);
            return;
        }
        
        UpdateLoginStatus("Verifying password...", Color.yellow);
        
        // Verify password against stored profile
        if (selectedProfile != null)
        {
            if (VerifyPassword(password, selectedProfile.password))
            {
                UpdateLoginStatus("Password verified!", Color.green);
                
                // Password correct, authenticate with server
                SelectProfile(selectedProfile);
                
                // Small delay for user feedback, then authenticate
                StartCoroutine(AuthenticateAfterDelay(username, password, 0.5f));
            }
            else
            {
                ShowNotification("Incorrect password");
                UpdateLoginStatus("Incorrect password", Color.red);
                return;
            }
        }
        else
        {
            ShowNotification("Profile not found");
            UpdateLoginStatus("Profile not found", Color.red);
            return;
        }
    }
    
    private void UpdateLoginStatus(string message, Color color)
    {
        if (loginStatusText != null)
        {
            loginStatusText.text = message;
            loginStatusText.color = color;
        }
    }
    
    private void AuthenticateWithServer(string username, string password)
    {
        ShowConnectionPanel("Authenticating...");
        
        if (SecureNetworkManager.Instance != null)
        {
            // Set the credentials in NetworkManager
            SecureNetworkManager.Instance.SetCredentials(username, password);
            
            // If not connected, connect first
            if (!SecureNetworkManager.Instance.IsConnected)
            {
                _ = SecureNetworkManager.Instance.ConnectToServerAsync();
            }
            else
            {
                // Already connected, just authenticate
                ShowMultiplayerPanel();
                HideConnectionPanel();
                ShowNotification("Ready to play!");
            }
        }
        else
        {
            ShowNotification("Network manager not available");
            HideConnectionPanel();
        }
    }
    
    private string EncryptPassword(string password)
    {
        // Simple encryption for local storage (you might want to use a more secure method in production)
        byte[] data = System.Text.Encoding.UTF8.GetBytes(password);
        return System.Convert.ToBase64String(data);
    }
    
    private bool VerifyPassword(string password, string encryptedPassword)
    {
        try
        {
            byte[] data = System.Convert.FromBase64String(encryptedPassword);
            string decrypted = System.Text.Encoding.UTF8.GetString(data);
            return password == decrypted;
        }
        catch
        {
            return false;
        }
    }
    
    private void DeleteProfile(ProfileData profile)
    {
        if (profile != null)
        {
            ShowConfirmationDialog(
                $"Are you sure you want to delete the profile '{profile.name}'?\n\nThis action cannot be undone.",
                () => ConfirmDeleteProfile(profile),
                () => HideConfirmationDialog()
            );
        }
        else
        {
            ShowNotification("Profile not found");
        }
    }
    
    private void ConfirmDeleteProfile(ProfileData profile)
    {
        savedProfiles.Remove(profile);
        SaveProfiles();
        ShowNotification($"Profile {profile.name} deleted");
        
        // Clear current profile if it was the deleted one
        if (selectedProfile == profile)
        {
            selectedProfile = null;
            playerName = "Player";
            playerId = null;
            if (currentProfileText != null)
                currentProfileText.text = "Profile: None";
        }
        
        // Refresh profile list
        RefreshProfileList();
        HideConfirmationDialog();
    }
    
    public void ShowConfirmationDialog(string message, System.Action onConfirm, System.Action onCancel)
    {
        if (confirmationPanel == null)
        {
            // Fallback to simple notification
            ShowNotification("Confirmation dialog not available. " + message);
            return;
        }
        
        confirmationPanel.SetActive(true);
        
        if (confirmationText != null)
        {
            confirmationText.text = message;
        }
        
        // Clear previous listeners and set new ones
        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.RemoveAllListeners();
            confirmYesButton.onClick.AddListener(() => onConfirm?.Invoke());
        }
        
        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.RemoveAllListeners();
            confirmNoButton.onClick.AddListener(() => onCancel?.Invoke());
        }
    }
    
    public void HideConfirmationDialog()
    {
        if (confirmationPanel != null)
        {
            confirmationPanel.SetActive(false);
        }
    }
    
    // Back button methods for new panels
    public void BackFromRegister()
    {
        ShowProfilePanel();
    }
    
    public void BackFromLogin()
    {
        ShowProfilePanel();
    }
}