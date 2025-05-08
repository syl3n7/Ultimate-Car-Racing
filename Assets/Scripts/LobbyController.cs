using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UltimateCarRacing.Networking;
using Newtonsoft.Json;
using UnityEngine.SceneManagement; 
public class LobbyController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject lobbyPanel;
    public GameObject roomPanel;
    public Button hostButton;
    public Button refreshButton;
    public Button startButton;
    public Button leaveButton;
    public TMP_InputField playerNameInput;
    public Transform serverListContent;
    public GameObject serverEntryPrefab;
    public TMP_Text roomInfoText;
    public TMP_Text connectionStatusText;
    public TMP_Text publicIdText;
    public TMP_Text debugText;

    [Header("Settings")]
    public float serverRefreshInterval = 5f;
    public string gameSceneName = "GameScene";
    public string defaultRoomName = "Race Room";

    private float lastRefreshTime;
    private string localPlayerName = "Player";
    private Dictionary<string, NetworkManager.GameRoom> availableRooms = 
        new Dictionary<string, NetworkManager.GameRoom>();
    private List<string> playersInRoom = new List<string>();

    private float lastRefreshRequestTime = 0f;
    private const float MIN_REFRESH_INTERVAL = 0.5f; // Minimum seconds between refreshes

    void Awake()
    {
        // Initialize UI
        lobbyPanel.SetActive(true);
        roomPanel.SetActive(false);
        
        // Load player name from prefs
        localPlayerName = PlayerPrefs.GetString("PlayerName", "Player");
        playerNameInput.text = localPlayerName;
        
        // Setup button listeners
        hostButton.onClick.AddListener(OnHostClicked);
        refreshButton.onClick.AddListener(RefreshRoomList);
        startButton.onClick.AddListener(OnStartClicked);
        leaveButton.onClick.AddListener(OnLeaveClicked);
        
        playerNameInput.onEndEdit.AddListener(OnNameChanged);
    }

    void Start()
    {
        // Load saved player name
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            localPlayerName = PlayerPrefs.GetString("PlayerName");
            if (playerNameInput != null)
            {
                playerNameInput.text = localPlayerName;
            }
        }
        
        // Register for network events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnServerListReceived += HandleServerList;
            NetworkManager.Instance.OnMessageReceived += HandleNetworkMessage;
            NetworkManager.Instance.OnPlayerJoined += HandlePlayerJoined;
            NetworkManager.Instance.OnConnectionStatusChanged += HandleConnectionStatusChanged;
            
            // Check initial connection status
            HandleConnectionStatusChanged(NetworkManager.Instance.ConnectionStatus, string.Empty);
        }
        
        // Set up UI event handlers
        if (playerNameInput != null)
        {
            playerNameInput.onEndEdit.AddListener(OnNameChanged);
        }
        
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(OnHostClicked);
        }
        
        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(() => RefreshRoomList());
        }
        
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }
        
        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(OnLeaveClicked);
        }
        
        // Don't refresh immediately - wait for connection and use a coroutine
        StartCoroutine(DelayedRoomRefresh());
    }

    private IEnumerator DelayedRoomRefresh()
    {
        // Wait for the NetworkManager to establish connection (up to 5 seconds)
        float startTime = Time.time;
        while (Time.time - startTime < 5.0f && 
              (NetworkManager.Instance == null || 
               NetworkManager.Instance.ConnectionStatus != NetworkConnectionState.Connected))
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        // Now request the game list if we're connected
        if (NetworkManager.Instance != null && 
            NetworkManager.Instance.ConnectionStatus == NetworkConnectionState.Connected)
        {
            RefreshRoomList();
        }
    }

    void Update()
    {
        // Periodically refresh server list when in lobby
        if (lobbyPanel.activeSelf && Time.time - lastRefreshTime > serverRefreshInterval)
        {
            RefreshRoomList();
        }

        // Update public ID display
        if (publicIdText != null && NetworkManager.Instance.ConnectionStatus == NetworkConnectionState.Connected)
        {
            publicIdText.text = $"Connected to Relay";
        }
        else
        {
            publicIdText.text = "Not Connected";
        }

        // Debug display
        if (debugText != null && NetworkManager.Instance != null)
        {
            debugText.text = $"My ID: {NetworkManager.Instance.ClientId}\n" +
                             $"Room: {(NetworkManager.Instance.IsHost ? "Host" : "Client")}\n" +
                             $"Players: {playersInRoom.Count}";
        }
    }

    void OnDestroy()
    {
        // Clean up network events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnServerListReceived -= HandleServerList;
            NetworkManager.Instance.OnMessageReceived -= HandleNetworkMessage;
            NetworkManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
            NetworkManager.Instance.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
        }
    }

    void OnNameChanged(string newName)
    {
        localPlayerName = newName;
        PlayerPrefs.SetString("PlayerName", localPlayerName);
    }

    void OnHostClicked()
    {
        // Use player name as room name if available
        string roomName = string.IsNullOrEmpty(localPlayerName) ? defaultRoomName : $"{localPlayerName}'s Room";
        
        NetworkManager.Instance.HostGame(roomName);
        
        // Switch to room view
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
        
        // Update room info
        playersInRoom.Clear();
        playersInRoom.Add($"{localPlayerName} (Host)");
        UpdateRoomInfo();
    }

    void OnStartClicked()
    {
        // Check for null references
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("Cannot start game: NetworkManager.Instance is null");
            return;
        }
        
        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("Cannot start game: SceneTransitionManager.Instance is null");
            
            // Try to find the SceneTransitionManager in the scene
            SceneTransitionManager transitionManager = FindObjectOfType<SceneTransitionManager>();
            if (transitionManager != null)
            {
                Debug.Log("Found SceneTransitionManager, but its Instance property is not set correctly");
            }
            else
            {
                Debug.Log("Creating SceneTransitionManager...");
                // Try to create a SceneTransitionManager
                GameObject managerObj = new GameObject("SceneTransitionManager");
                transitionManager = managerObj.AddComponent<SceneTransitionManager>();
                
                // Wait for it to initialize
                StartCoroutine(LoadSceneAfterManagerInitialized(GetSelectedTrackName()));
                return;
            }
            return;
        }
        
        // Use selected track instead of hardcoded "GameOn"
        string trackToLoad = GetSelectedTrackName();
        
        // Tell all players to load the game scene
        NetworkManager.Instance.SendMessageToRoom($"LOAD_SCENE|{trackToLoad}");
        
        // Load locally
        SceneTransitionManager.Instance.LoadScene(trackToLoad);
    }

    private IEnumerator LoadSceneAfterManagerInitialized(string sceneName)
    {
        // Wait for SceneTransitionManager to be initialized
        yield return new WaitForSeconds(0.5f);
        
        if (SceneTransitionManager.Instance != null)
        {
            // Tell all players to load the game scene
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.SendMessageToRoom($"LOAD_SCENE|{sceneName}");
            }
            
            // Load locally
            SceneTransitionManager.Instance.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("SceneTransitionManager.Instance is still null after initialization attempt");
            
            // Fallback: use direct scene loading
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
    }

    void OnLeaveClicked()
    {
        // Only the host should tell others the room is closed
        if (NetworkManager.Instance.IsHost)
        {
            // Tell all players the room is closed
            NetworkManager.Instance.SendMessageToRoom("ROOM_CLOSED");
            Debug.Log("Host closed room, notifying all players");
        }
        else
        {
            // Non-host players just leave silently
            Debug.Log("Client leaving room");
        }
        
        // Explicitly leave the room on the server
        NetworkManager.Instance.LeaveRoom();
        
        // Return to lobby
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
        
        // Refresh room list
        RefreshRoomList();
    }

    private void RefreshRoomList()
    {
        // Only request game list if connected and not too soon after last request
        if (NetworkManager.Instance != null && 
            NetworkManager.Instance.ConnectionStatus == NetworkConnectionState.Connected &&
            Time.time - lastRefreshRequestTime > MIN_REFRESH_INTERVAL)
        {
            NetworkManager.Instance.RequestGameList();
            lastRefreshTime = Time.time;
            lastRefreshRequestTime = Time.time;
            Debug.Log("Sent refresh request for room list");
        }
        else
        {
            Debug.Log($"Skipped refresh - either not connected or too soon (last: {Time.time - lastRefreshRequestTime:F1}s ago)");
        }
    }

    void UpdateRoomInfo()
    {
        if (roomInfoText == null) return;

        string info = "Players in Room:\n";
        foreach (string player in playersInRoom)
        {
            info += $"- {player}\n";
        }

        roomInfoText.text = info;
    }

    void HandleConnectionStatusChanged(NetworkConnectionState status, string message)
    {
        // Update UI based on connection status
        if (connectionStatusText != null)
        {
            switch (status)
            {
                case NetworkConnectionState.Connected:
                    connectionStatusText.text = "Connected to Relay";
                    connectionStatusText.color = Color.green;
                    break;
                    
                case NetworkConnectionState.Connecting:
                    connectionStatusText.text = "Connecting...";
                    connectionStatusText.color = Color.yellow;
                    break;
                    
                case NetworkConnectionState.Failed:
                    connectionStatusText.text = "Connection Failed";
                    connectionStatusText.color = Color.red;
                    break;
                    
                case NetworkConnectionState.Disconnected:
                    connectionStatusText.text = "Disconnected";
                    connectionStatusText.color = Color.red;
                    break;
            }
        }
    }

    void HandleServerList(List<NetworkManager.GameRoom> rooms)
    {
        // Update available rooms
        availableRooms.Clear();
        foreach (var room in rooms)
        {
            availableRooms[room.roomId] = room;
        }
        
        // Update UI
        UpdateRoomListUI();
    }

    void HandlePlayerJoined(string clientId)
    {
        // Add player to our list if not already present
        if (!playersInRoom.Contains(clientId))
        {
            playersInRoom.Add(clientId);
            
            // Update the room info display
            UpdateRoomInfo();
            
            // Tell the new player our name
            NetworkManager.Instance.SendMessageToPlayer(clientId, $"PLAYER_INFO|{localPlayerName}");
        }
    }

    void HandleNetworkMessage(string fromClient, string message)
    {
        string[] parts = message.Split('|');
        if (parts.Length == 0) return;
        
        string command = parts[0];
        string[] args = parts.Length > 1 ? new string[parts.Length - 1] : new string[0];
        
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = parts[i + 1];
        }
        
        switch (command)
        {
            case "PLAYER_INFO":
                // Update player name in our list
                HandlePlayerInfo(fromClient, args);
                break;
                
            case "LOAD_SCENE":
                // Load the specified scene
                HandleLoadScene(args);
                break;
                
            case "ROOM_CLOSED":
                // Return to lobby
                HandleRoomClosed();
                break;
        }
    }

    void HandlePlayerInfo(string clientId, string[] args)
    {
        if (args.Length < 1) return;
        
        string playerName = args[0];
        
        // Update the player's name in our list
        for (int i = 0; i < playersInRoom.Count; i++)
        {
            if (playersInRoom[i] == clientId)
            {
                playersInRoom[i] = playerName;
                break;
            }
        }
        
        // Update room info display
        UpdateRoomInfo();
    }

    void HandleLoadScene(string[] args)
    {
        if (args.Length < 1) return;
        
        // Load the specified scene
        UnityEngine.SceneManagement.SceneManager.LoadScene(args[0]);
    }

    void HandleRoomClosed()
    {
        // Return to lobby
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
        
        // Refresh room list
        RefreshRoomList();
    }

    void UpdateRoomListUI()
    {
        // Clear existing UI elements first
        foreach (Transform child in serverListContent)
        {
            Destroy(child.gameObject);
        }
        
        Debug.Log($"Updating room list UI with {availableRooms.Count} rooms");
        
        // Create UI entries for each room
        foreach (var roomEntry in availableRooms)
        {
            var room = roomEntry.Value;
            
            // Log each room to verify there are no duplicates
            Debug.Log($"Adding room to UI: {room.roomId} - {room.name} ({room.playerCount}/{room.maxPlayers})");
            
            GameObject entryObj = Instantiate(serverEntryPrefab, serverListContent);
            ServerListEntry entry = entryObj.GetComponent<ServerListEntry>();
            
            if (entry != null)
            {
                entry.InitializeEntry(
                    room.name,
                    room.hostId,
                    $"{room.playerCount}/{room.maxPlayers}",
                    () => JoinSelectedRoom(room.roomId)
                );
            }
        }
    }

    void JoinSelectedRoom(string roomId)
    {
        NetworkManager.Instance.JoinGame(roomId);
        
        // Switch to room view
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
        
        // Initialize our player list with just our name for now
        playersInRoom.Clear();
        playersInRoom.Add(localPlayerName);
        UpdateRoomInfo();
    }

    private void OnStartGameClicked()
    {
        // Wait a moment to ensure all clients are ready
        StartCoroutine(StartWithDelay());
    }

    private IEnumerator StartWithDelay()
    {
        // Wait a moment to let messages propagate
        yield return new WaitForSeconds(1.0f);
        
        // Send starting message to all players
        NetworkManager.Instance.SendMessageToRoom("STARTING_GAME");
        
        // Wait a bit more before actually starting
        yield return new WaitForSeconds(1.0f);

        // Only the host needs to send the START_GAME command
        if (NetworkManager.Instance.IsHost)
        {
            // The GameManager will handle this message and spawn players
            List<string> playerIds = new List<string>();
            
            // Add the host
            playerIds.Add(NetworkManager.Instance.ClientId);
            
            // Add all connected players
            foreach (var player in NetworkManager.Instance.ConnectedPlayers)
            {
                if (!playerIds.Contains(player.clientId))
                {
                    playerIds.Add(player.clientId);
                }
            }
            
            // Log the player list
            Debug.Log($"Starting game with players: {String.Join(", ", playerIds)}");
            
            // Send this to all clients
            string playersJson = JsonConvert.SerializeObject(playerIds.ToArray());
            NetworkManager.Instance.SendMessageToRoom($"START_GAME|{playersJson}");
        }
        
        // Load game scene
        SceneManager.LoadScene(gameSceneName);
    }

    // Add this helper method
    private string GetSelectedTrackName()
    {
        // Default to "GameOn" if nothing is selected
        string trackName = "GameOn";
        
        if (GameManager.SelectedTrackIndex > 0)
        {
            // Use SceneUtility to get the actual scene name by build index
            string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(GameManager.SelectedTrackIndex);
            if (!string.IsNullOrEmpty(scenePath))
            {
                trackName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                Debug.Log($"Using selected track: {trackName} (index: {GameManager.SelectedTrackIndex})");
            }
            else
            {
                Debug.LogWarning($"Invalid track index: {GameManager.SelectedTrackIndex}, using default");
            }
        }
        
        return trackName;
    }
}