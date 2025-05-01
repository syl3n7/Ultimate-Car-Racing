using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltimateCarRacing.Networking;
using Newtonsoft.Json;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Player Setup")]
    public GameObject playerCarPrefab;
    public Transform[] spawnPoints;
    public float respawnHeight = 2f; // Height above spawn point
    
    [Header("Network Settings")]
    public float syncInterval = 0.1f; // How often to send full sync (seconds)
    public float inputSyncInterval = 0.05f; // How often to send input (seconds)
    
    [Header("Debug Options")]
    public bool useDebugSpawnPosition = false;
    public Vector3 debugSpawnPosition = new Vector3(50f, 10f, 50f);
    
    private Dictionary<string, PlayerController> activePlayers = new Dictionary<string, PlayerController>();
    private string localPlayerId;
    private float lastSyncTime;
    private float lastInputSyncTime;
    private bool gameStarted = false;
    
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
        // Register network callbacks
        NetworkManager.Instance.OnMessageReceived += HandleNetworkMessage;
        NetworkManager.Instance.OnGameDataReceived += HandleGameData;
        
        // Get the local player ID
        localPlayerId = NetworkManager.Instance.ClientId;
        
        // Find spawn points if not assigned in inspector
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            FindSpawnPoints();
        }
        
        // TEST: Force spawn after 2 seconds - comment this out after testing!
        StartCoroutine(DelayedTestSpawn());
        
        // We're ready to start
        SendReadyMessage();
    }
    
    private void FindSpawnPoints()
    {
        // Look for a parent object containing spawn points
        GameObject spawnPointsParent = GameObject.Find("SpawnPoints");
        
        if (spawnPointsParent != null)
        {
            // Get all child transforms
            List<Transform> points = new List<Transform>();
            foreach (Transform child in spawnPointsParent.transform)
            {
                points.Add(child);
            }
            
            if (points.Count > 0)
            {
                spawnPoints = points.ToArray();
                Debug.Log($"Found {points.Count} spawn points in scene");
            }
        }
        
        // If we still don't have spawn points, look for objects tagged "SpawnPoint"
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            GameObject[] taggedPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (taggedPoints.Length > 0)
            {
                spawnPoints = new Transform[taggedPoints.Length];
                for (int i = 0; i < taggedPoints.Length; i++)
                {
                    spawnPoints[i] = taggedPoints[i].transform;
                }
                Debug.Log($"Found {taggedPoints.Length} spawn points tagged as 'SpawnPoint'");
            }
        }
        
        // If we still don't have spawn points, create default ones
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("No spawn points found, creating default spawn points");
            CreateDefaultSpawnPoints();
        }
    }
    
    private void CreateDefaultSpawnPoints()
    {
        // Create a parent object for organization
        GameObject parent = new GameObject("SpawnPoints");
        
        // Create 4 spawn points in a line
        spawnPoints = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject point = new GameObject($"SpawnPoint{i+1}");
            point.transform.SetParent(parent.transform);
            
            // Position them in a line with some spacing
            point.transform.position = new Vector3(i * 5f, 0.5f, 0f);
            
            // Make them all face the same direction
            point.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            
            spawnPoints[i] = point.transform;
        }
    }
    
    private IEnumerator WaitForClientId()
    {
        // Wait for the client ID to be set (max 10 seconds)
        float startTime = Time.time;
        while (string.IsNullOrEmpty(NetworkManager.Instance.ClientId) && Time.time - startTime < 10f)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        // Check if we got the client ID
        localPlayerId = NetworkManager.Instance.ClientId;
        if (!string.IsNullOrEmpty(localPlayerId))
        {
            Debug.Log($"Received client ID: {localPlayerId}");
            SendReadyMessage();
        }
        else
        {
            Debug.LogError("Failed to get client ID from server after timeout.");
        }
    }
    
    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived -= HandleNetworkMessage;
            NetworkManager.Instance.OnGameDataReceived -= HandleGameData;
        }
    }
    
    void Update()
    {
        if (!gameStarted) return;
        
        // Periodically sync full state
        if (Time.time - lastSyncTime > syncInterval)
        {
            SyncPlayerState();
            lastSyncTime = Time.time;
        }
        
        // Sync inputs more frequently for smoother gameplay
        if (Time.time - lastInputSyncTime > inputSyncInterval)
        {
            SyncPlayerInput();
            lastInputSyncTime = Time.time;
        }
    }
    
    private void SendReadyMessage()
    {
        // Tell host we're ready to start
        NetworkManager.Instance.SendMessageToRoom("PLAYER_READY");
    }
    
    private void SyncPlayerState()
    {
        if (!activePlayers.ContainsKey(localPlayerId)) return;
        
        var playerCar = activePlayers[localPlayerId];
        
        // Create state data
        PlayerStateData stateData = new PlayerStateData
        {
            Position = playerCar.transform.position,
            Rotation = playerCar.transform.rotation.eulerAngles,
            Velocity = playerCar.Rigidbody.velocity,  // FIXED: Changed from linearVelocity to velocity
            AngularVelocity = playerCar.Rigidbody.angularVelocity,
            Timestamp = Time.time
        };
        
        // Serialize and send
        string jsonData = JsonConvert.SerializeObject(stateData);
        NetworkManager.Instance.SendGameDataToRoom($"STATE|{jsonData}");
    }
    
    private void SyncPlayerInput()
    {
        if (!activePlayers.ContainsKey(localPlayerId)) return;
        
        var playerCar = activePlayers[localPlayerId];
        
        // Only send input if local player is actively controlling the car
        if (playerCar.HasInputChanges)
        {
            PlayerInputData inputData = new PlayerInputData
            {
                Throttle = playerCar.CurrentThrottle,
                Steering = playerCar.CurrentSteering,
                Brake = playerCar.CurrentBrake,
                Timestamp = Time.time
            };
            
            string jsonData = JsonConvert.SerializeObject(inputData);
            NetworkManager.Instance.SendGameDataToRoom($"INPUT|{jsonData}");
            
            // Reset input change flag
            playerCar.HasInputChanges = false;
        }
    }
    
    public void SpawnPlayers(string[] playerIds)
    {
        Debug.Log($"SpawnPlayers called with {playerIds.Length} players");
        
        if (playerCarPrefab == null)
        {
            Debug.LogError("Player car prefab is null! Cannot spawn players.");
            return;
        }
        
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points available! Cannot spawn players.");
            return;
        }
        
        gameStarted = true;
        
        // Find a spawn point for each player
        for (int i = 0; i < playerIds.Length; i++)
        {
            string playerId = playerIds[i];
            int spawnIndex = i % spawnPoints.Length; // Wrap around if more players than spawn points
            
            SpawnPlayer(playerId, spawnIndex);
        }
    }
    
    private void SpawnPlayer(string playerId, int spawnIndex)
    {
        // Determine spawn position based on debug settings
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        
        if (useDebugSpawnPosition)
        {
            // Use the debug spawn position
            spawnPosition = debugSpawnPosition;
            spawnRotation = Quaternion.identity; // Default rotation
            Debug.Log($"Using debug spawn position {debugSpawnPosition} for player {playerId}");
        }
        else
        {
            // Use normal spawn points
            spawnPosition = spawnPoints[spawnIndex].position + Vector3.up * respawnHeight;
            spawnRotation = spawnPoints[spawnIndex].rotation;
        }
        
        // Instantiate the player car
        GameObject playerObj = Instantiate(playerCarPrefab, spawnPosition, spawnRotation);
        PlayerController playerController = playerObj.GetComponent<PlayerController>();
        
        // Setup the player controller
        playerController.Initialize(playerId, playerId == localPlayerId);
        
        // Add to active players
        activePlayers[playerId] = playerController;
        
        // If this is the local player, enable its camera and disable any other cameras
        if (playerId == localPlayerId)
        {
            SetupLocalPlayerCamera(playerObj);
        }
        else
        {
            // For remote players, ensure their cameras are disabled
            DisableRemotePlayerCamera(playerObj);
        }
        
        Debug.Log($"Spawned player {playerId} at position {spawnPosition}");
    }

    private void SetupLocalPlayerCamera(GameObject playerObj)
    {
        // Find any camera in the scene that might be active
        Camera[] sceneCameras = FindObjectsOfType<Camera>();
        foreach (Camera cam in sceneCameras)
        {
            // Disable any camera that's not on our player car
            if (!cam.transform.IsChildOf(playerObj.transform))
            {
                cam.gameObject.SetActive(false);
            }
        }
        
        // Find and enable the camera on the player car
        Camera playerCamera = playerObj.GetComponentInChildren<Camera>();
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
            playerCamera.tag = "MainCamera";
            
            Debug.Log("Activated local player camera");
        }
        else
        {
            Debug.LogWarning("No camera found on player car prefab!");
        }
    }

    private void DisableRemotePlayerCamera(GameObject playerObj)
    {
        // Disable any cameras on remote player objects
        Camera playerCamera = playerObj.GetComponentInChildren<Camera>();
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(false);
        }
    }
    
    private void HandleNetworkMessage(string fromClient, string message)
    {
        Debug.Log($"Received network message: {message} from {fromClient}");
        
        string[] parts = message.Split('|');
        if (parts.Length < 1) return;
        
        string command = parts[0];
        
        switch (command)
        {
            case "PLAYER_READY":
                Debug.Log($"Player ready: {fromClient}");
                // Host handles player ready messages
                if (NetworkManager.Instance.IsHost)
                {
                    CheckAllPlayersReady();
                }
                break;
                
            case "START_GAME":
                Debug.Log($"Start game command received with data: {(parts.Length > 1 ? parts[1] : "none")}");
                // Game starting, spawn players
                try {
                    string[] playerIds = JsonConvert.DeserializeObject<string[]>(parts[1]);
                    Debug.Log($"Deserialized {playerIds.Length} player IDs: {string.Join(", ", playerIds)}");
                    SpawnPlayers(playerIds);
                }
                catch (Exception ex) {
                    Debug.LogError($"Error deserializing player IDs: {ex.Message}");
                }
                break;
                
            case "RESPAWN":
                // Handle player respawn
                RespawnPlayer(fromClient);
                break;
        }
    }
    
    private void HandleGameData(string fromClient, string jsonData)
    {
        // Ignore our own data
        if (fromClient == localPlayerId) return;
        
        try
        {
            string[] parts = jsonData.Split('|', 2);
            if (parts.Length < 2) return;
            
            string dataType = parts[0];
            string data = parts[1];
            
            switch (dataType)
            {
                case "STATE":
                    // Handle full state update
                    PlayerStateData stateData = JsonConvert.DeserializeObject<PlayerStateData>(data);
                    OptimizePlayerStateSync(fromClient, stateData);
                    break;
                    
                case "INPUT":
                    // Handle input update (for prediction)
                    PlayerInputData inputData = JsonConvert.DeserializeObject<PlayerInputData>(data);
                    if (activePlayers.ContainsKey(fromClient) && !activePlayers[fromClient].IsLocal)
                    {
                        activePlayers[fromClient].ApplyRemoteInput(inputData);
                    }
                    break;
                    
                case "EVENT":
                    // Handle game events like respawns, powerups, etc.
                    HandleGameEvent(fromClient, data);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing game data: {ex.Message}");
        }
    }
    
    private void OptimizePlayerStateSync(string playerId, PlayerStateData stateData)
    {
        // Skip sync for inactive players
        if (!activePlayers.ContainsKey(playerId))
            return;
        
        var player = activePlayers[playerId];
        
        // For remote players, we need to update their state
        if (!player.IsLocal)
        {
            // Calculate the time since this state was generated
            float latency = Time.time - stateData.Timestamp;
            
            // If we get an old state (out of order packets), ignore it
            if (player.LastStateTimestamp > stateData.Timestamp)
                return;
            
            player.LastStateTimestamp = stateData.Timestamp;
            
            // For small position changes, use interpolation
            float distanceDiff = Vector3.Distance(player.transform.position, stateData.Position);
            
            // If the difference is too large, teleport to avoid weird movement
            if (distanceDiff > player.desyncThreshold)
            {
                player.ApplyRemoteState(stateData, true); // true = force teleport
            }
            else
            {
                player.ApplyRemoteState(stateData, false); // false = use interpolation
            }
        }
    }
    
    private void HandleGameEvent(string fromClient, string eventData)
    {
        try
        {
            Dictionary<string, object> eventInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(eventData);
            string eventType = eventInfo["event"].ToString();
            
            switch (eventType)
            {
                case "respawn":
                    Vector3 position = JsonUtility.FromJson<Vector3>(eventInfo["position"].ToString());
                    Quaternion rotation = JsonUtility.FromJson<Quaternion>(eventInfo["rotation"].ToString());
                    
                    if (activePlayers.ContainsKey(fromClient))
                    {
                        activePlayers[fromClient].Respawn(position, rotation);
                    }
                    break;
                    
                // Add other event types as needed
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error handling game event: {ex.Message}");
        }
    }
    
    private void HandlePlayerState(string playerId, string stateJson)
    {
        // Deserialize the state data
        PlayerStateData stateData = JsonConvert.DeserializeObject<PlayerStateData>(stateJson);
        
        // Make sure the player exists
        if (!activePlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"Received state for unknown player: {playerId}");
            return;
        }
        
        // Apply state to remote player
        var player = activePlayers[playerId];
        if (!player.IsLocal)
        {
            player.ApplyRemoteState(stateData);
        }
    }
    
    private void HandlePlayerInput(string playerId, string inputJson)
    {
        // Deserialize the input data
        PlayerInputData inputData = JsonConvert.DeserializeObject<PlayerInputData>(inputJson);
        
        // Make sure the player exists
        if (!activePlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"Received input for unknown player: {playerId}");
            return;
        }
        
        // Apply input to remote player for prediction
        var player = activePlayers[playerId];
        if (!player.IsLocal)
        {
            player.ApplyRemoteInput(inputData);
        }
    }
    
    private void CheckAllPlayersReady()
    {
        Debug.Log("CheckAllPlayersReady called");
        
        if (!NetworkManager.Instance.IsHost)
        {
            Debug.Log("Not host, ignoring ready check");
            return;
        }
        
        // In a real implementation, you would check if all connected players are ready
        // For simplicity, we'll just start the game after a short delay
        
        Debug.Log("Starting game after delay");
        StartCoroutine(StartGameAfterDelay(2f));
    }

    private IEnumerator StartGameAfterDelay(float delay)
    {
        Debug.Log($"Game will start in {delay} seconds");
        yield return new WaitForSeconds(delay);
        
        Debug.Log("Delay finished, preparing to start game");
        
        // Get all player IDs from NetworkManager
        List<string> playerIds = new List<string>();
        
        // Add the host (local player)
        if (string.IsNullOrEmpty(localPlayerId))
        {
            Debug.LogError("Local player ID is null or empty!");
            localPlayerId = "host_player"; // Fallback
        }
        
        playerIds.Add(localPlayerId);
        Debug.Log($"Added local player to list: {localPlayerId}");
        
        // Add all connected players
        var connectedPlayers = NetworkManager.Instance.ConnectedPlayers;
        if (connectedPlayers != null && connectedPlayers.Count > 0)
        {
            Debug.Log($"Found {connectedPlayers.Count} connected players");
            foreach (var player in connectedPlayers)
            {
                Debug.Log($"Adding remote player: {player.clientId}");
                playerIds.Add(player.clientId);
            }
        }
        else
        {
            Debug.Log("No connected players found");
        }
        
        // Serialize the player list
        string playersJson = JsonConvert.SerializeObject(playerIds.ToArray());
        Debug.Log($"Serialized player list: {playersJson}");
        
        // Send start game message to all players
        NetworkManager.Instance.SendMessageToRoom($"START_GAME|{playersJson}");
        Debug.Log("Sent START_GAME message to room");
        
        // Start the game locally
        Debug.Log("Starting game locally");
        SpawnPlayers(playerIds.ToArray());
    }
    
    private void RespawnPlayer(string playerId)
    {
        if (!activePlayers.ContainsKey(playerId)) return;
        
        // Find a random spawn point
        int spawnIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        Vector3 spawnPosition = spawnPoints[spawnIndex].position + Vector3.up * respawnHeight;
        Quaternion spawnRotation = spawnPoints[spawnIndex].rotation;
        
        var player = activePlayers[playerId];
        player.Respawn(spawnPosition, spawnRotation);
        
        Debug.Log($"Respawned player {playerId} at spawn point {spawnIndex}");
    }
    
    // Data structures for network serialization
    
    [System.Serializable]
    public class PlayerStateData
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public float Timestamp;
    }
    
    [System.Serializable]
    public class PlayerInputData
    {
        public float Throttle;
        public float Steering;
        public float Brake;
        public float Timestamp;
    }

    // Add this to GameManager
    public void TestSpawnSinglePlayer()
    {
        // Use a test player ID if we don't have a real one
        string testId = string.IsNullOrEmpty(localPlayerId) ? "test_player" : localPlayerId;
        SpawnPlayers(new string[] { testId });
    }

    private IEnumerator DelayedTestSpawn()
    {
        yield return new WaitForSeconds(2f);
        Debug.Log("Forcing test player spawn");
        TestSpawnSinglePlayer();
    }
}