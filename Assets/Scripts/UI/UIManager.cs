using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    
    [Header("Main Menu Panels")]
    public GameObject mainMenuPanel;
    public GameObject multiplayerMenuPanel;
    public GameObject hostGamePanel;
    public GameObject joinGamePanel;
    public GameObject roomLobbyPanel;
    
    [Header("Host Game UI")]
    public TMP_InputField roomNameInput;
    public Slider maxPlayersSlider;
    public TextMeshProUGUI maxPlayersText;
    
    [Header("Join Game UI")]
    public Transform roomListContent;
    public GameObject roomListItemPrefab;
    public Button refreshRoomsButton;
    
    [Header("Room Lobby UI")]
    public TextMeshProUGUI roomInfoText;
    public Transform playerListContent;
    public GameObject playerListItemPrefab;
    public Button startGameButton;
    
    [Header("Connection UI")]
    public GameObject connectionPanel;
    public TextMeshProUGUI connectionStatusText;
    
    [Header("Notification UI")]
    public GameObject notificationPanel;
    public TextMeshProUGUI notificationText;
    
    private List<Dictionary<string, object>> roomList = new List<Dictionary<string, object>>();
    private List<string> playersInRoom = new List<string>();
    private string currentRoomId;
    private string currentRoomName;
    private bool isHost = false;
    
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
    
    // UI Navigation
    public void ShowMainMenu()
    {
        HideAllPanels();
        mainMenuPanel.SetActive(true);
    }
    
    public void ShowMultiplayerMenu()
    {
        HideAllPanels();
        multiplayerMenuPanel.SetActive(true);
        
        // Connect to server if not already connected
        if (NetworkClient.Instance != null && !NetworkClient.Instance.IsConnected())
        {
            ShowConnectionPanel("Connecting to server...");
            NetworkClient.Instance.Connect();
        }
    }
    
    public void ShowHostGamePanel()
    {
        HideAllPanels();
        hostGamePanel.SetActive(true);
        
        // Set default values
        roomNameInput.text = "Race Room";
        maxPlayersSlider.value = 8;
    }
    
    public void ShowJoinGamePanel()
    {
        HideAllPanels();
        joinGamePanel.SetActive(true);
        
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
        
        // Show/hide start game button based on host status
        startGameButton.gameObject.SetActive(isHost);
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
        multiplayerMenuPanel.SetActive(false);
        hostGamePanel.SetActive(false);
        joinGamePanel.SetActive(false);
        roomLobbyPanel.SetActive(false);
        connectionPanel.SetActive(false);
        notificationPanel.SetActive(false);
    }
    
    // Actions
    public void HostGame()
    {
        if (NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            string roomName = roomNameInput.text;
            if (string.IsNullOrEmpty(roomName))
                roomName = "Race Room";
                
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
            ShowMultiplayerMenu();
            
            // Reset room state
            currentRoomId = null;
            currentRoomName = null;
            isHost = false;
            playersInRoom.Clear();
        }
    }
    
    public void DisconnectFromServer()
    {
        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.Disconnect();
        }
        
        ShowMainMenu();
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
    
    // Network event handlers
    private void OnConnected()
    {
        HideConnectionPanel();
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
            currentRoomName = roomNameInput.text;
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
    
    private void UpdateRoomInfo()
    {
        roomInfoText.text = $"Room: {currentRoomName} ({playersInRoom.Count} players)";
        
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
            
            string playerName = playerId;
            if (playerId == NetworkClient.Instance.GetClientId())
                playerName += " (You)";
                
            playerText.text = playerName;
        }
    }
}