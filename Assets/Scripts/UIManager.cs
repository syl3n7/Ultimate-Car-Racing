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
        
        // Register for network events
        NetworkClient networkClient = NetworkClient.Instance;
        if (networkClient != null)
        {
            networkClient.OnConnected += OnConnected;
            networkClient.OnDisconnected += OnDisconnected;
            networkClient.OnConnectionFailed += OnConnectionFailed;
            networkClient.OnRoomListReceived += OnRoomListReceived;
            networkClient.OnGameHosted += OnGameHosted;
            networkClient.OnJoinedGame += OnJoinedGame;
            networkClient.OnPlayerJoined += OnPlayerJoined;
            networkClient.OnPlayerDisconnected += OnPlayerDisconnected;
            networkClient.OnGameStarted += OnGameStarted;
            networkClient.OnServerMessage += OnServerMessage;
        }
        
        // Set up UI callbacks
        maxPlayersSlider.onValueChanged.AddListener(OnMaxPlayersSliderChanged);
        refreshRoomsButton.onClick.AddListener(RefreshRoomList);
        
        // Initialize max players text
        OnMaxPlayersSliderChanged(maxPlayersSlider.value);
    }
    
    void OnDestroy()
    {
        // Unregister network events
        NetworkClient networkClient = NetworkClient.Instance;
        if (networkClient != null)
        {
            networkClient.OnConnected -= OnConnected;
            networkClient.OnDisconnected -= OnDisconnected;
            networkClient.OnConnectionFailed -= OnConnectionFailed;
            networkClient.OnRoomListReceived -= OnRoomListReceived;
            networkClient.OnGameHosted -= OnGameHosted;
            networkClient.OnJoinedGame -= OnJoinedGame;
            networkClient.OnPlayerJoined -= OnPlayerJoined;
            networkClient.OnPlayerDisconnected -= OnPlayerDisconnected;
            networkClient.OnGameStarted -= OnGameStarted;
            networkClient.OnServerMessage -= OnServerMessage;
        }
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
        if (NetworkClient.Instance != null && !NetworkClient.Instance.IsConnected())
        {
            ShowConnectionPanel("Connecting to server...");
            NetworkClient.Instance.Connect();
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
        
        // Generate a player ID - ideally this would include some hardware-specific information
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
        // Clear existing items
        foreach (Transform child in profileListContent)
        {
            Destroy(child.gameObject);
        }
        
        // Add profiles
        foreach (ProfileData profile in savedProfiles)
        {
            GameObject profileItem = Instantiate(profileListItemPrefab, profileListContent);
            
            // Set profile name and ID text
            TextMeshProUGUI nameText = profileItem.transform.Find("NameText").GetComponent<TextMeshProUGUI>();
            nameText.text = profile.name;
            
            TextMeshProUGUI infoText = profileItem.transform.Find("InfoText").GetComponent<TextMeshProUGUI>();
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
    
    public void CreateRoom()
    {
        if (NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            string roomName = createRoomNameInput.text;
            if (string.IsNullOrEmpty(roomName))
                roomName = $"{playerName}'s Room";
                
            int maxPlayers = (int)maxPlayersSlider.value;
            
            ShowConnectionPanel("Creating room...");
            NetworkClient.Instance.HostGame(roomName, maxPlayers);
        }
    }
    
    public void JoinSelectedRoom()
    {
        if (currentRoomId != null && NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            ShowConnectionPanel("Joining room...");
            NetworkClient.Instance.JoinGame(currentRoomId);
        }
        else
        {
            ShowNotification("Please select a room to join");
        }
    }
    
    public void StartGame()
    {
        if (isHost && NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            ShowConnectionPanel("Starting game...");
            NetworkClient.Instance.StartGame();
        }
    }
    
    public void LeaveRoom()
    {
        if (NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            NetworkClient.Instance.LeaveGame();
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
    
    public void RefreshRoomList()
    {
        if (NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            ClearRoomList();
            NetworkClient.Instance.RequestRoomList();
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
        roomInfoText.text = $"Room: {currentRoomName}";
        playerCountText.text = $"Players: {playersInRoom.Count}";
        
        // Show/hide start game button based on host status
        startGameButton.gameObject.SetActive(isHost);
        
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
            
            string playerDisplayName = playerId;
            if (playerId == NetworkClient.Instance.GetClientId())
                playerDisplayName += " (You)";
                
            playerText.text = playerDisplayName;
        }
    }
    
    #endregion
    
    #region Network Event Handlers
    
    private void OnConnected()
    {
        HideConnectionPanel();
        
        // Send player name to server
        if (NetworkClient.Instance != null)
        {
            Dictionary<string, object> playerInfo = new Dictionary<string, object>
            {
                { "type", "PLAYER_INFO" },
                { "name", playerName },
                { "id", playerId }
            };
            
            NetworkClient.Instance.SendTcpMessage(playerInfo);
        }
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
        
        // Parse room list from message
        if (message.ContainsKey("rooms"))
        {
            var rooms = message["rooms"] as Newtonsoft.Json.Linq.JArray;
            if (rooms != null)
            {
                foreach (var roomObject in rooms)
                {
                    var room = roomObject.ToObject<Dictionary<string, object>>();
                    
                    // Create room list item
                    GameObject roomItem = Instantiate(roomListItemPrefab, roomListContent);
                    RoomListItem roomListItem = roomItem.GetComponent<RoomListItem>();
                    
                    string roomId = room["room_id"].ToString();
                    string roomName = room["name"].ToString();
                    int playerCount = Convert.ToInt32(room["player_count"]);
                    int maxPlayers = Convert.ToInt32(room["max_players"]);
                    
                    roomListItem.Initialize(roomId, roomName, playerCount, maxPlayers);
                    roomListItem.OnSelected += OnRoomSelected;
                }
            }
        }
    }
    
    private void OnRoomSelected(string roomId, string roomName)
    {
        currentRoomId = roomId;
        currentRoomName = roomName;
    }
    
    private void OnGameHosted(Dictionary<string, object> message)
    {
        HideConnectionPanel();
        
        if (message.ContainsKey("room_id"))
        {
            currentRoomId = message["room_id"].ToString();
            currentRoomName = createRoomNameInput.text;
            isHost = true;
            
            // Add self to players list
            string clientId = NetworkClient.Instance.GetClientId();
            playersInRoom.Clear();
            playersInRoom.Add(clientId);
            
            ShowRoomLobbyPanel();
            ShowNotification("Room created successfully");
        }
    }
    
    private void OnJoinedGame(Dictionary<string, object> message)
    {
        HideConnectionPanel();
        
        if (message.ContainsKey("room_id") && message.ContainsKey("host_id"))
        {
            currentRoomId = message["room_id"].ToString();
            
            // Determine if we're the host
            string hostId = message["host_id"].ToString();
            string clientId = NetworkClient.Instance.GetClientId();
            isHost = (hostId == clientId);
            
            // Add self to players list
            playersInRoom.Clear();
            playersInRoom.Add(clientId);
            
            ShowRoomLobbyPanel();
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
        
        if (message.ContainsKey("spawn_position"))
        {
            var spawnPosObj = message["spawn_position"] as Newtonsoft.Json.Linq.JObject;
            
            // Extract spawn position
            Vector3 spawnPosition = new Vector3(
                Convert.ToSingle(spawnPosObj["x"]),
                Convert.ToSingle(spawnPosObj["y"]),
                Convert.ToSingle(spawnPosObj["z"])
            );
            
            // Hide UI and load game scene
            HideAllPanels();
            
            // Set spawn position in GameManager
            if (GameManager.Instance != null)
            {
                // Set the spawn position before loading the scene
                GameManager.Instance.SetMultiplayerSpawnPosition(spawnPosition);
                
                // Load the selected track scene
                int trackIndex = GameManager.SelectedTrackIndex;
                string sceneName = $"Track_{trackIndex + 1}";
                SceneManager.LoadScene(sceneName);
            }
        }
    }
    
    private void OnServerMessage(Dictionary<string, object> message)
    {
        if (message.ContainsKey("message"))
        {
            string serverMessage = message["message"].ToString();
            ShowNotification($"Server: {serverMessage}");
        }
    }
    
    #endregion
}