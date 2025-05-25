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

    [Header("Authentication UI")]
    public GameObject authPanel;
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public Button loginButton;
    public TextMeshProUGUI authStatusText;
    
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
        NetworkManager networkManager = NetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected += (msg) => OnConnected();
            networkManager.OnDisconnected += (msg) => OnDisconnected();
            networkManager.OnConnectionFailed += (msg) => OnConnectionFailed();
            networkManager.OnRoomListReceived += OnRoomListReceived;
            networkManager.OnGameHosted += OnGameHosted;
            networkManager.OnRoomJoined += OnJoinedGame;
            networkManager.OnPlayerJoined += OnPlayerJoined;
            networkManager.OnPlayerDisconnected += OnPlayerDisconnected;
            networkManager.OnGameStarted += OnGameStarted;
            networkManager.OnServerMessage += OnServerMessage;
            networkManager.OnRoomPlayersReceived += OnRoomPlayersReceived;
        }
        
        // Verify required references
        if (profileListContent == null)
        {
            Debug.LogError("Profile list content is not assigned in the Inspector!");
        }
        
        if (profileListItemPrefab == null)
        {
            Debug.LogError("Profile list item prefab is not assigned in the Inspector!");
        }

        // Initialize max players text
        OnMaxPlayersSliderChanged(maxPlayersSlider.value);

        // After your existing ConnectAllUIButtons call
    
        // Explicitly connect the Create Room button for certainty
        Button createRoomButton = FindObjectOfType<Button>(true);
        if (createRoomButton != null && createRoomButton.name == "CreateRoomButton")
        {
            createRoomButton.onClick.RemoveAllListeners();
            createRoomButton.onClick.AddListener(CreateRoom);
            Debug.Log("Explicitly connected CreateRoomButton");
        }

        CheckPanelReferences();

        // Connect auth panel buttons
        if (loginButton != null)
        {
            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(OnLoginButtonClicked);
        }
    }
    
    private void ConnectAllUIButtons()
    {
        Debug.Log("Connecting all UI buttons automatically");
        
        // Main Menu buttons
        ConnectButton("PlayButton", OnPlayButtonClicked);
        ConnectButton("InstructionsButton", ShowInstructions);
        ConnectButton("CreditsButton", ShowCredits);
        ConnectButton("ProfileButton", ShowProfilePanel);
        ConnectButton("ExitButton", ExitGame);
        
        // Profile panel buttons
        ConnectButton("CreateProfileButton", CreateNewProfile);
        ConnectButton("BackToMainButton", ShowMainMenu);
        
        // Multiplayer panel buttons
        ConnectButton("CreateGameButton", ShowRoomListPanel);
        ConnectButton("JoinGameButton", ShowRoomListPanel);
        ConnectButton("BackFromMultiplayerButton", ShowMainMenu);
        
        // Room list panel buttons
        ConnectButton("CreateRoomButton", CreateRoom);
        ConnectButton("JoinRoomButton", JoinSelectedRoom);
        ConnectButton("RefreshRoomsButton", RefreshRoomList);
        ConnectButton("BackFromRoomListButton", ShowMultiplayerPanel);
        
        // Add explicit listener to Refresh button if it exists in Inspector
        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.onClick.RemoveAllListeners();
            refreshRoomsButton.onClick.AddListener(RefreshRoomList);
            Debug.Log("Explicitly connected RefreshRoomsButton from inspector reference");
        }
        
        // Room lobby panel buttons
        ConnectButton("StartGameButton", StartGame);
        ConnectButton("LeaveRoomButton", LeaveRoom);
        
        // Find and connect sliders
        Slider[] allSliders = FindObjectsOfType<Slider>(true);
        foreach (var slider in allSliders)
        {
            if (slider.name == "MaxPlayersSlider")
            {
                Debug.Log("Found MaxPlayersSlider, connecting onValueChanged event");
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(OnMaxPlayersSliderChanged);
                
                // Set max value to 20
                slider.maxValue = 20;
                
                // Make sure the text is updated with the initial value
                OnMaxPlayersSliderChanged(slider.value);
            }
        }
    }

    private void ConnectButton(string buttonName, UnityEngine.Events.UnityAction action)
    {
        // Find all buttons in the scene (including inactive ones)
        Button[] allButtons = FindObjectsOfType<Button>(true);
        
        foreach (var button in allButtons)
        {
            if (button.name == buttonName)
            {
                Debug.Log($"Connected button: {buttonName}");
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(action);
                return;
            }
        }
        
        // If we get here, the button wasn't found
        Debug.LogWarning($"Button not found: {buttonName}");
    }
    
    // In OnDestroy method - replace NetworkClient references with NetworkManager
    void OnDestroy()
    {
        // Unregister network events
        NetworkManager networkManager = NetworkManager.Instance;
        if (networkManager != null)
        {
            networkManager.OnConnected -= (msg) => OnConnected();
            networkManager.OnDisconnected -= (msg) => OnDisconnected();
            networkManager.OnConnectionFailed -= (msg) => OnConnectionFailed();
            networkManager.OnRoomListReceived -= OnRoomListReceived;
            networkManager.OnGameHosted -= OnGameHosted;
            networkManager.OnRoomJoined -= OnJoinedGame;
            networkManager.OnPlayerJoined -= OnPlayerJoined;
            networkManager.OnPlayerDisconnected -= OnPlayerDisconnected;
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
        if (NetworkManager.Instance != null && !NetworkManager.Instance.IsConnected())
        {
            ShowConnectionPanel("Connecting to server...");
            // Use non-TLS connection first, then upgrade if needed
            _ = NetworkManager.Instance.Connect(useTls: false);
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
    }
    else
    {
        // Profile exists, go directly to game mode selection or single player
        // You can modify this to go to a game mode selection screen instead
        ShowMultiplayerPanel();
    }
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
        Debug.Log("ShowRoomLobbyPanel called - transitioning to lobby view");
    
        // Check if the panel exists
        if (roomLobbyPanel == null)
        {
            Debug.LogError("roomLobbyPanel is null! Cannot show room lobby panel.");
            return;
        }
        
        HideAllPanels();
        roomLobbyPanel.SetActive(true);
        
        // Verify activation
        if (!roomLobbyPanel.activeSelf)
        {
            Debug.LogError("Failed to activate roomLobbyPanel!");
        }
        else
        {
            Debug.Log("Room lobby panel is now active");
        }
        
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
        
        // Also hide auth panel if it exists
        if (authPanel != null)
        {
            authPanel.SetActive(false);
        }
        
        // Don't hide race UI panels here - they're controlled by scene changes
    }

    public void ShowAuthPanel(string message = "Please login to continue")
    {
        if (authPanel == null)
        {
            Debug.LogError("Auth panel is not assigned in the Inspector!");
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
        
        if (NetworkManager.Instance != null)
        {
            // Set the credentials in NetworkManager
            NetworkManager.Instance.SetCredentials(username, password);
            
            // Update local player name and attempt to reconnect
            playerName = username;
            
            // Show connection panel
            ShowConnectionPanel("Authenticating...");
            
            // If not connected, connect
            if (!NetworkManager.Instance.IsConnected())
            {
                _ = NetworkManager.Instance.Connect();
            }
        }
    }
    
    #endregion
    
    #region Profile Management
    
    public void CreateNewProfile()
    {
        Debug.Log("CreateNewProfile called");
        string name = playerNameInput.text;
        if (string.IsNullOrEmpty(name))
        {
            ShowNotification("Please enter a name");
            Debug.Log("Name was empty");
            return;
        }
        
        Debug.Log($"Creating profile for: {name}");
        
        // Generate a player ID
        string id = GenerateUniquePlayerId(name);
        
        // Create new profile
        ProfileData profile = new ProfileData(name, id);
        savedProfiles.Add(profile);
        
        // Save to disk
        SaveProfiles();
        
        // Set as current profile
        SelectProfile(profile);
        
        Debug.Log("Profile created, showing multiplayer panel");
        
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
        Debug.LogError("Profile list content transform is not assigned in the Inspector!");
        return;
    }
    
    // Check if profileListItemPrefab is assigned
    if (profileListItemPrefab == null)
    {
        Debug.LogError("Profile list item prefab is not assigned in the Inspector!");
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
            Debug.LogError("NameText child object not found in profile item prefab!");
            continue;
        }
        
        TextMeshProUGUI nameText = nameTextTransform.GetComponent<TextMeshProUGUI>();
        if (nameText == null)
        {
            Debug.LogError("TextMeshProUGUI component not found on NameText child!");
            continue;
        }
        nameText.text = profile.name;
        
        Transform infoTextTransform = profileItem.transform.Find("InfoText");
        if (infoTextTransform == null)
        {
            Debug.LogError("InfoText child object not found in profile item prefab!");
            continue;
        }
        
        TextMeshProUGUI infoText = infoTextTransform.GetComponent<TextMeshProUGUI>();
        if (infoText == null)
        {
            Debug.LogError("TextMeshProUGUI component not found on InfoText child!");
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
        else
        {
            Debug.LogError("Button component not found on profile item prefab!");
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
    Debug.Log("CreateRoom function called");

    // Make sure we have a valid track selected
    if (GameManager.SelectedTrackIndex < 0)
    {
        GameManager.SelectedTrackIndex = 0; // Set to default track
    }
    
    if (NetworkManager.Instance == null)
    {
        Debug.LogError("NetworkManager.Instance is null");
        ShowNotification("Network manager not available");
        return;
    }
    
    // Show connection panel immediately to provide feedback
    ShowConnectionPanel("Creating room...");
    
    // Make sure we're connected before attempting to create a room
    if (!NetworkManager.Instance.IsConnected())
    {
        Debug.Log("Not connected, connecting first...");
        ShowConnectionPanel("Connecting to server...");
        
        try {
            // Actually await the connection this time
            await NetworkManager.Instance.Connect();
            
            // Check if we're connected after the await
            if (!NetworkManager.Instance.IsConnected()) {
                ShowNotification("Could not connect to server");
                HideConnectionPanel();
                return;
            }
        }
        catch (Exception e) {
            Debug.LogError($"Connection error: {e.Message}");
            ShowNotification("Connection error: " + e.Message);
            HideConnectionPanel();
            return;
        }
    }
    
    string roomName = createRoomNameInput.text;
    if (string.IsNullOrEmpty(roomName))
        roomName = $"{playerName}'s Room";
        
    int maxPlayers = (int)maxPlayersSlider.value;
    
    Debug.Log($"Creating room: {roomName}, Max players: {maxPlayers}");
    NetworkManager.Instance.HostGame(roomName, maxPlayers);
}
    
    // In JoinSelectedRoom method - update to use NetworkManager
    public void JoinSelectedRoom()
    {
        if (currentRoomId != null && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected())
        {
            ShowConnectionPanel("Joining room...");
            NetworkManager.Instance.JoinGame(currentRoomId);
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
        
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected() && !string.IsNullOrEmpty(currentRoomId))
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
            
            Debug.Log($"Host is starting game for room: {currentRoomId} with {playersInRoom.Count} players");
            
            // Send the start game command according to server documentation
            NetworkManager.Instance.StartGame();
        }
        else
        {
            ShowNotification("Cannot start game - connection issue");
            Debug.LogError("Start game failed: Not connected or no room ID");
        }
    }
    
    public void LeaveRoom()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected())
        {
            NetworkManager.Instance.LeaveGame();
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
    public void RefreshRoomList()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected())
        {
            Debug.Log("Requesting room list from server");
            ClearRoomList();
            
            // Show a temporary "Loading..." message
            GameObject loadingText = new GameObject("LoadingText");
            loadingText.transform.SetParent(roomListContent, false);
            TextMeshProUGUI textComponent = loadingText.AddComponent<TextMeshProUGUI>();
            textComponent.text = "Loading rooms...";
            textComponent.fontSize = 24;
            textComponent.alignment = TextAlignmentOptions.Center;
            
            NetworkManager.Instance.RequestRoomList();
        }
        else
        {
            Debug.LogError("Cannot refresh room list - not connected to server");
            ShowNotification("Not connected to server");
        }
    }
    
    private void ClearRoomList()
    {
        Debug.Log("Clearing room list UI");
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
        // Debug log commented for cleaner console
        // Debug.Log($"UpdateRoomInfo called - Room: {currentRoomName}, Players: {playersInRoom.Count}, isHost: {isHost}");
    
        // Check references
        if (roomInfoText == null)
        {
            Debug.LogError("roomInfoText is null! Cannot update room info text.");
            return;
        }
    
        if (playerCountText == null)
        {
            Debug.LogError("playerCountText is null! Cannot update player count text.");
            return;
        }
    
        if (startGameButton == null)
        {
            Debug.LogError("startGameButton is null! Cannot update start game button visibility.");
            return;
        }
    
        if (playerListContent == null)
        {
            Debug.LogError("playerListContent is null! Cannot update player list.");
            return;
        }
    
        if (playerListItemPrefab == null)
        {
            Debug.LogError("playerListItemPrefab is null! Cannot create player list items.");
            return;
        }
    
        // Update UI elements
        roomInfoText.text = $"Room: {currentRoomName}";
        playerCountText.text = $"Players: {playersInRoom.Count}";
    
        // Show/hide start game button based on host status
        startGameButton.gameObject.SetActive(isHost);
        // Debug.Log($"Start game button visibility set to {isHost} (isHost={isHost})");
        
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
        // Debug.Log($"Adding {playersInRoom.Count} players to player list UI");
        foreach (string playerId in playersInRoom)
        {
            // Debug.Log($"Creating player item for: {playerId}");
            GameObject playerItem = Instantiate(playerListItemPrefab, playerListContent);
            TextMeshProUGUI playerText = playerItem.GetComponentInChildren<TextMeshProUGUI>();
    
            if (playerText == null)
            {
                Debug.LogError("TextMeshProUGUI component not found on player list item!");
                continue;
            }
    
            string playerDisplayName = playerId;
            if (NetworkManager.Instance != null && playerId == NetworkManager.Instance.GetClientId())
                playerDisplayName += " (You)";
    
            playerText.text = playerDisplayName;
            // Debug.Log($"Added player to UI: {playerDisplayName}");
        }
    
        // Debug.Log("Room info updated successfully");
    }
    
    #endregion
    
    #region Network Event Handlers
    
    private void OnConnected()
    {
        HideConnectionPanel();
        
        // According to SERVER-README.md section 3.2, only NAME is needed for registration
        // NetworkManager already sent the NAME command during connection
        
        // Just for extra in-game information, we can use PLAYER_INFO command to get details
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.RequestPlayerInfo();
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
                        string roomId = room["room_id"].ToString();
                        string roomName = room["name"].ToString();
                        
                        // Handle player_count numeric conversion safely
                        int playerCount = 0;
                        if (room.ContainsKey("player_count"))
                        {
                            if (room["player_count"] is int intVal)
                                playerCount = intVal;
                            else
                                int.TryParse(room["player_count"].ToString(), out playerCount);
                        }
                        
                        // Handle max_players numeric conversion safely  
                        int maxPlayers = 20;
                        if (room.ContainsKey("max_players"))
                        {
                            if (room["max_players"] is int intVal)
                                maxPlayers = intVal;
                            else
                                int.TryParse(room["max_players"].ToString(), out maxPlayers);
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
            string clientId = NetworkManager.Instance.GetClientId();
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
                string clientId = NetworkManager.Instance.GetClientId();
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

    // In OnJoinedGame method - update to use NetworkManager
    private void OnJoinedGame(Dictionary<string, object> message)
    {
        HideConnectionPanel();

        if (message.ContainsKey("room_id") && message.ContainsKey("host_id"))
        {
            currentRoomId = message["room_id"].ToString();

            // Determine if we're the host
            string hostId = message["host_id"].ToString();
            string clientId = NetworkManager.Instance.GetClientId();
            isHost = (hostId == clientId);
            
            Debug.Log($"Joined room: ID={currentRoomId}, ClientID={clientId}, HostID={hostId}, isHost={isHost}");

            // CHANGE: Don't clear the player list, and request player list from server
            playersInRoom.Clear();
            playersInRoom.Add(clientId); // Add self

            // Request the complete player list from the server
            if (NetworkManager.Instance != null)
            {
                // Use the proper helper method
                NetworkManager.Instance.GetRoomPlayers(currentRoomId);
                Debug.Log($"Sent request for room players: {currentRoomId}");
            }

            ShowRoomLobbyPanel();
            UpdateRoomInfo();
            ShowNotification("Joined room successfully");
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
    
    private void OnPlayerDisconnected(Dictionary<string, object> message)
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
        
        if (message.ContainsKey("spawn_position"))
        {
            var spawnPosObj = message["spawn_position"] as Newtonsoft.Json.Linq.JObject;
            
            // Extract spawn position
            Vector3 spawnPosition = new Vector3(
                Convert.ToSingle(spawnPosObj["x"]),
                Convert.ToSingle(spawnPosObj["y"]),
                Convert.ToSingle(spawnPosObj["z"])
            );
            
            // Extract spawn index if available
            int spawnIndex = 0;
            if (spawnPosObj.ContainsKey("index"))
            {
                spawnIndex = Convert.ToInt32(spawnPosObj["index"]);
            }
            
            Debug.Log($"Game started with spawn position: {spawnPosition}, index: {spawnIndex}");
            
            if (GameManager.Instance != null)
            {
                // Store the spawn position in the GameManager for the race scene to use
                GameManager.Instance.SetPlayerSpawnPosition(spawnPosition, spawnIndex);
                
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
            Debug.LogError("Spawn position data missing in game start message!");
            ShowNotification("Error: Missing spawn data. Please try again.");
        }
    }
    
    // Add a method to configure TLS settings
    public void ConfigureConnection(string address, int port, bool useSecureConnection = true, string certHash = "")
    {
        this.useTLS = useSecureConnection;
        this.serverCertificateHash = certHash;
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ConfigureConnection(address, port, useSecureConnection, certHash);
            Debug.Log($"Connection configured: Server={address}:{port}, TLS={useSecureConnection}, CertHash={certHash}");
        }
        else
        {
            Debug.LogError("Cannot configure connection - NetworkManager.Instance is null");
            ShowNotification("Error: Network manager not found. Please restart the game.");
        }
    }
    
    // Method to connect with appropriate security settings
    public void ConnectToServer(string address, int port)
    {
        ShowNotification("Connecting to server...");
        
        // Default to secure connection if not specified otherwise
        ConfigureConnection(address, port, true);
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.Connect(address, port, (success, message) => {
                if (success)
                {
                    ShowNotification("Connected to server!");
                }
                else
                {
                    // If connection fails with TLS, try without TLS as fallback
                    if (useTLS && message.Contains("TLS"))
                    {
                        Debug.LogWarning("TLS connection failed, attempting non-secure fallback");
                        ShowNotification("Secure connection failed. Trying alternate connection...");
                        
                        // Try again without TLS
                        ConfigureConnection(address, port, false);
                        NetworkManager.Instance.Connect(address, port, (fallbackSuccess, fallbackMessage) => {
                            if (fallbackSuccess)
                            {
                                ShowNotification("Connected to server (non-secure)!");
                            }
                            else
                            {
                                Debug.LogError($"Failed to connect: {fallbackMessage}");
                                ShowNotification($"Connection failed: {fallbackMessage}");
                            }
                        });
                    }
                    else
                    {
                        Debug.LogError($"Failed to connect: {message}");
                        ShowNotification($"Connection failed: {message}");
                    }
                }
            });
        }
        else
        {
            Debug.LogError("Cannot connect - NetworkManager.Instance is null");
            ShowNotification("Error: Network manager not found. Please restart the game.");
        }
    }
    
    // Add a coroutine to handle scene loading asynchronously
    private IEnumerator LoadRaceSceneAsync(string sceneName)
    {
        Debug.Log($"Starting async scene load for {sceneName}");
        
        // Begin loading the scene
        AsyncOperation asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        
        // Don't allow scene activation until we're ready (optional)
        // asyncLoad.allowSceneActivation = false;
        
        // Show loading progress
        while (!asyncLoad.isDone)
        {
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            Debug.Log($"Loading progress: {progress * 100}%");
            
            // If we're close to done, allow activation
            // if (asyncLoad.progress >= 0.9f)
            // {
            //     asyncLoad.allowSceneActivation = true;
            // }
            
            yield return null;
        }
        
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

    // Add explicit check for panel references
    private void CheckPanelReferences()
    {
        Debug.Log("Checking UI panel references...");
        
        if (mainMenuPanel == null) Debug.LogError("mainMenuPanel is null!");
        if (multiplayerPanel == null) Debug.LogError("multiplayerPanel is null!");
        if (roomListPanel == null) Debug.LogError("roomListPanel is null!");
        if (roomLobbyPanel == null) Debug.LogError("roomLobbyPanel is null!");
        if (connectionPanel == null) Debug.LogError("connectionPanel is null!");
        if (notificationPanel == null) Debug.LogError("notificationPanel is null!");
    }

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
        if (latencyText != null && NetworkManager.Instance != null && Time.time - lastLatencyUpdateTime > LATENCY_UPDATE_INTERVAL)
        {
            float latency = NetworkManager.Instance.GetLatency();
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
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected() && !string.IsNullOrEmpty(currentRoomId))
        {
            // Use the proper helper method that formats the command correctly
            NetworkManager.Instance.GetRoomPlayers(currentRoomId);
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