using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltimateCarRacing.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Player Setup")]
    public GameObject playerCarPrefab;
    public Transform[] spawnPoints;
    public float respawnHeight = 2f; // Height above spawn point
    public int maxPlayers = 4;  // Add this for calculating spawn positions
    
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
    private GameObject localPlayerObject;
    
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
        NetworkManager.Instance.OnPositionReset += HandlePositionReset; // Ensure reset is registered here too
        
        // Get the local player ID immediately and log it
        localPlayerId = NetworkManager.Instance.ClientId;
        Debug.Log($"Local player ID set to: {localPlayerId}");
        
        // If ID is null or empty, wait for it to be assigned
        if (string.IsNullOrEmpty(localPlayerId)) 
        {
            StartCoroutine(WaitForClientId());
        }
        else
        {
            // Force spawn after a short delay if we already have an ID
            StartCoroutine(DelayedTestSpawn());
        }
    }

    void OnEnable()
    {
        // Listen for position reset commands
        NetworkManager.Instance.OnPositionReset += HandlePositionReset;
    }

    void OnDisable()
    {
        // Remove listener when disabled
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnPositionReset -= HandlePositionReset;
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
        // Check if local player's car has gone missing
        if (gameStarted && localPlayerObject != null)
        {
            if (localPlayerObject == null)
            {
                Debug.LogError("Local player object is null even though it was previously assigned!");
            }
        }
        
        // Rest of update code...
        
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
        
        // Configure serialization settings to avoid circular references
        var settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                IgnoreSerializableInterface = true
            }
        };
        
        // Create player state using SerializableVector3 instead of Vector3
        var playerState = new PlayerStateData
        {
            Position = new SerializableVector3(playerCar.transform.position),
            Rotation = new SerializableVector3(playerCar.transform.eulerAngles),
            Velocity = new SerializableVector3(playerCar.Rigidbody.linearVelocity),
            AngularVelocity = new SerializableVector3(playerCar.Rigidbody.angularVelocity),
            Timestamp = Time.time
        };
        
        // Use the configured settings when serializing
        string jsonData = JsonConvert.SerializeObject(playerState, settings);
        
        // Send the data to other players
        NetworkManager.Instance.SendGameDataToRoom(jsonData);
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
        Debug.Log($"SpawnPlayers called with {playerIds.Length} players: {string.Join(", ", playerIds)}");
        
        if (playerCarPrefab == null)
        {
            Debug.LogError("Player car prefab is null! Cannot spawn players.");
            return;
        }
        
        // Clear any existing players first to prevent duplicates
        bool hadPlayers = activePlayers.Count > 0;
        if (hadPlayers)
        {
            Debug.Log("Clearing existing players before spawning new ones");
            // Only destroy remote player objects, not local player
            foreach (var kvp in activePlayers)
            {
                if (kvp.Key != localPlayerId && kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
        }
        
        // Start with a clean dictionary if we didn't have players before
        if (!hadPlayers)
        {
            activePlayers.Clear();
        }
        
        gameStarted = true;
        
        // Find a spawn point for each player
        for (int i = 0; i < playerIds.Length; i++)
        {
            string playerId = playerIds[i];
            int spawnIndex = i % spawnPoints.Length; // Wrap around if more players than spawn points
            
            // Skip if this player is already spawned (avoid respawning the local player)
            if (activePlayers.ContainsKey(playerId) && activePlayers[playerId] != null)
            {
                Debug.Log($"Player {playerId} already exists, skipping spawn");
                continue;
            }
            
            SpawnPlayer(playerId, spawnIndex);
        }
    }
    
    public GameObject SpawnPlayer(string playerId, int spawnIndex = 0)
    {
        // Debug who is being spawned
        Debug.Log($"Spawning player: {playerId}, local player is: {localPlayerId}, isLocal={playerId == localPlayerId}");
        
        // Use configurable spawn positions
        Vector3 spawnPosition;
        
        if (useDebugSpawnPosition || spawnIndex == 0)
        {
            spawnPosition = debugSpawnPosition;
        }
        else
        {
            // Calculate different positions for additional players
            float angle = spawnIndex * (360f / maxPlayers);
            float radius = 5f;
            spawnPosition = new Vector3(
                debugSpawnPosition.x + radius * Mathf.Cos(angle * Mathf.Deg2Rad),
                debugSpawnPosition.y,
                debugSpawnPosition.z + radius * Mathf.Sin(angle * Mathf.Deg2Rad)
            );
        }
        
        // Create the player object
        GameObject playerObject = Instantiate(playerCarPrefab, spawnPosition, Quaternion.identity);
        playerObject.name = $"Car_{playerId}"; // Give unique name for debugging
        
        // Set up player controller
        PlayerController controller = playerObject.GetComponent<PlayerController>();
        if (controller != null)
        {
            // Initialize with correct ownership
            bool isLocalPlayer = (playerId == localPlayerId);
            controller.Initialize(playerId, isLocalPlayer);
            Debug.Log($"Initialized car for {playerId}, isLocalPlayer={isLocalPlayer}");
            
            activePlayers[playerId] = controller;
            
            // Setup camera only for local player
            if (isLocalPlayer)
            {
                SetupLocalPlayerCamera(playerObject);
                localPlayerObject = playerObject;
                Debug.Log($"Set {playerId} as local player with camera");
            }
            else
            {
                DisableRemotePlayerCamera(playerObject);
                Debug.Log($"Set {playerId} as remote player without camera");
            }
        }
        
        Debug.Log($"Spawned player {playerId} at position ({spawnPosition.x:F2}, {spawnPosition.y:F2}, {spawnPosition.z:F2})");
        return playerObject;
    }

    private void SetupLocalPlayerCamera(GameObject playerObj)
    {
        Debug.Log($"Setting up camera for local player: {localPlayerId}");
        
        // Find the camera on the player object
        Camera playerCamera = playerObj.GetComponentInChildren<Camera>(true);
        if (playerCamera == null)
        {
            Debug.LogError("No camera found on player car prefab!");
            return;
        }
        
        // Activate this camera
        playerCamera.gameObject.SetActive(true);
        playerCamera.tag = "MainCamera";
        
        // Disable all other cameras in the scene
        Camera[] allCameras = FindObjectsOfType<Camera>();
        foreach (Camera cam in allCameras)
        {
            // Skip the player's camera
            if (cam == playerCamera) continue;
            
            // Otherwise disable this camera
            cam.gameObject.SetActive(false);
            Debug.Log($"Disabled camera: {cam.gameObject.name}");
        }
        
        // Make sure audio listener is set up
        AudioListener audioListener = playerCamera.GetComponent<AudioListener>();
        if (audioListener == null)
        {
            playerCamera.gameObject.AddComponent<AudioListener>();
        }
        else
        {
            audioListener.enabled = true;
        }
        
        Debug.Log($"Local player camera activated: {playerCamera.gameObject.name}");
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
                    Debug.Log($"Deserialized {playerIds.Length} player IDs: {String.Join(", ", playerIds)}");
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
        yield return new WaitForSeconds(delay);
        
        // Get all player IDs from NetworkManager
        List<string> playerIds = new List<string>();
        
        // Always add the local player ID first
        Debug.Log($"Adding local player to spawn list: {localPlayerId}");
        playerIds.Add(localPlayerId);
        
        // Add all connected players from NetworkManager
        var connectedPlayers = NetworkManager.Instance.ConnectedPlayers;
        foreach (var player in connectedPlayers)
        {
            // Skip if this is our own ID (already added)
            if (player.clientId == localPlayerId)
                continue;
                
            // Add this remote player
            Debug.Log($"Adding remote player to spawn list: {player.clientId}");
            playerIds.Add(player.clientId);
        }
        
        // Log the complete player list for debugging
        Debug.Log($"Starting game with {playerIds.Count} players: {string.Join(", ", playerIds)}");
        
        // Serialize and send player list to all clients
        string playersJson = JsonConvert.SerializeObject(playerIds.ToArray());
        NetworkManager.Instance.SendMessageToRoom($"START_GAME|{playersJson}");
        
        // Start the game locally
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
        public SerializableVector3 Position;
        public SerializableVector3 Rotation;
        public SerializableVector3 Velocity;
        public SerializableVector3 AngularVelocity;
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

    private void HandlePositionReset(Vector3 newPosition)
    {
        Debug.Log($"GameManager received position reset request to {newPosition}");
        
        // Reset position of local player
        if (localPlayerObject != null)
        {
            Debug.Log($"Applying position reset to local player {localPlayerId}");
            
            // Force disable and re-enable the controller or rigidbody to reset physics state
            var rb = localPlayerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                bool wasKinematic = rb.isKinematic;
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                localPlayerObject.transform.position = newPosition;
                rb.isKinematic = wasKinematic;
                Debug.Log($"Reset position using Rigidbody to {newPosition}");
            }
            else
            {
                localPlayerObject.transform.position = newPosition;
                Debug.Log($"Reset position by direct transform to {newPosition}");
            }
            
            // Also broadcast this change to other players
            if (NetworkManager.Instance != null)
            {
                var eventData = new Dictionary<string, object> {
                    { "event", "teleport" },
                    { "position", newPosition }
                };
                string jsonData = JsonConvert.SerializeObject(eventData);
                NetworkManager.Instance.SendGameDataToRoom($"EVENT|{jsonData}");
                Debug.Log("Broadcast position reset to other players");
            }
        }
        else
        {
            Debug.LogError("Can't reset position - localPlayerObject is null!");
        }
    }

    // only for testing purposes
    public void TestResetPosition()
    {
        // Manually trigger the position reset
        HandlePositionReset(new Vector3(50f, 10f, 50f));
    }

    private void OnConnectionStatusChanged(NetworkConnectionState status, string message)
    {
        if (status == NetworkConnectionState.Connected)
        {
            // When connected, get our client ID
            localPlayerId = NetworkManager.Instance.ClientId;
            Debug.Log($"Connected to server! Local player ID: {localPlayerId}");
        }
    }
}