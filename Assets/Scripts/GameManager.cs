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
    
    
    void Update()
    {
        // Check if local player's car has gone missing
        if (gameStarted && localPlayerObject == null)
        {
            Debug.LogWarning("Local player object is missing! Attempting recovery...");
            EnsureLocalPlayerExists();
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

        
        if (Input.GetKeyDown(KeyCode.F8)) {
            Debug.Log("Forcing local player removal for testing");
            if (localPlayerObject != null) {
                Destroy(localPlayerObject);
                // Don't clean dictionaries - let the recovery handle it
            }
        }

        // Add this to Update method, right after the F8 test
        if (Input.GetKeyDown(KeyCode.F9)) {
            Debug.Log("Checking for missing players...");
            
            // Log connected players from NetworkManager
            if (NetworkManager.Instance != null) {
                var players = NetworkManager.Instance.ConnectedPlayers;
                Debug.Log($"Connected players according to NetworkManager: {players.Count}");
                foreach (var player in players) {
                    Debug.Log($"Player: {player.clientId}");
                    
                    // Check if this player exists in our dictionary
                    if (!activePlayers.ContainsKey(player.clientId)) {
                        Debug.Log($"Player {player.clientId} is connected but not spawned! Spawning now...");
                        SpawnPlayer(player.clientId);
                    }
                }
            }
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
        
        // Send the data to other players - CRITICAL FIX: properly format as STATE
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
        Debug.Log($"SpawnPlayers called with {playerIds.Length} players: {string.Join(", ", playerIds)}");
        
        if (playerCarPrefab == null)
        {
            Debug.LogError("Player car prefab is null! Cannot spawn players.");
            return;
        }
        
        // IMPORTANT: First check for existing players that need to be preserved
        HashSet<string> playerIdsToSpawn = new HashSet<string>(playerIds);
        HashSet<string> existingPlayerIds = new HashSet<string>(activePlayers.Keys);
        
        // Log the players we're dealing with
        Debug.Log($"Existing players: {string.Join(", ", existingPlayerIds)}");
        Debug.Log($"Players to ensure exist: {string.Join(", ", playerIdsToSpawn)}");
        
        // First handle any players that need to be removed
        List<string> playersToRemove = new List<string>();
        foreach (string existingId in existingPlayerIds)
        {
            if (!playerIdsToSpawn.Contains(existingId))
            {
                playersToRemove.Add(existingId);
            }
        }
        
        // Remove players that shouldn't exist anymore
        foreach (string removeId in playersToRemove)
        {
            Debug.Log($"Removing player that's no longer in the game: {removeId}");
            if (activePlayers[removeId] != null)
            {
                Destroy(activePlayers[removeId].gameObject);
            }
            activePlayers.Remove(removeId);
        }
        
        // Now, only spawn players that don't already exist
        List<string> newPlayersToSpawn = new List<string>();
        foreach (string playerId in playerIds)
        {
            if (!existingPlayerIds.Contains(playerId) || 
                activePlayers[playerId] == null || 
                activePlayers[playerId].gameObject == null)
            {
                newPlayersToSpawn.Add(playerId);
            }
            else
            {
                Debug.Log($"Player {playerId} already exists with valid object, preserving");
                
                // Ensure local player is properly flagged as local
                if (playerId == localPlayerId)
                {
                    var player = activePlayers[playerId];
                    if (!player.IsLocal)
                    {
                        Debug.Log($"Fixing ownership for player {playerId} - setting to local");
                        player.SetIsLocal(true);
                        SetupLocalPlayerCamera(player.gameObject);
                        localPlayerObject = player.gameObject;
                    }
                }
            }
        }
        
        // Now spawn only the new players
        for (int i = 0; i < newPlayersToSpawn.Count; i++)
        {
            string playerId = newPlayersToSpawn[i];
            int spawnIndex = i % spawnPoints.Length; // Wrap around if more players than spawn points
            
            SpawnPlayer(playerId, spawnIndex);
        }
        
        gameStarted = true;
    }
    
    public GameObject SpawnPlayer(string playerId, int spawnIndex = 0)
    {
        // Debug who is being spawned
        Debug.Log($"Spawning player: {playerId}, local player is: {localPlayerId}, isLocal={playerId == localPlayerId}");
        
        // Use configurable spawn positions
        Vector3 spawnPosition;
        
        if (useDebugSpawnPosition)
        {
            spawnPosition = debugSpawnPosition;
        }
        else if (spawnPoints != null && spawnPoints.Length > 0)
        {
            // Use actual spawn points if available
            int actualIndex = spawnIndex % spawnPoints.Length;
            spawnPosition = spawnPoints[actualIndex].position + Vector3.up * respawnHeight;
            Debug.Log($"Using spawn point {actualIndex} for player {playerId}");
        }
        else
        {
            // Fallback to debug position with offset based on index
            float angle = spawnIndex * (360f / maxPlayers);
            float radius = 5f;
            spawnPosition = new Vector3(
                debugSpawnPosition.x + radius * Mathf.Cos(angle * Mathf.Deg2Rad),
                debugSpawnPosition.y,
                debugSpawnPosition.z + radius * Mathf.Sin(angle * Mathf.Deg2Rad)
            );
            Debug.Log($"Using calculated spawn position for player {playerId}");
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
        
        // First enable the camera object in case it was disabled
        playerCamera.gameObject.SetActive(true);
        
        // Set the tag - this will make it the main camera
        playerCamera.tag = "MainCamera";
        
        // Disable all other cameras in the scene
        Camera[] allCameras = FindObjectsOfType<Camera>();
        foreach (Camera cam in allCameras)
        {
            // Skip the player's camera
            if (cam == playerCamera) continue;
            
            // Otherwise disable this camera and its audio listener
            cam.gameObject.SetActive(false);
            AudioListener otherListener = cam.GetComponent<AudioListener>();
            if (otherListener) otherListener.enabled = false;
            
            Debug.Log($"Disabled camera: {cam.gameObject.name}");
        }
        
        // Make sure audio listener is set up
        AudioListener audioListener = playerCamera.GetComponent<AudioListener>();
        if (audioListener == null)
        {
            audioListener = playerCamera.gameObject.AddComponent<AudioListener>();
        }
        
        // Make sure it's enabled
        audioListener.enabled = true;
        
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
                try {
                    string[] playerIds = JsonConvert.DeserializeObject<string[]>(parts[1]);
                    Debug.Log($"Deserialized {playerIds.Length} player IDs: {String.Join(", ", playerIds)}");
                    
                    // IMPORTANT: Only spawn if we haven't already or if the player list has changed
                    bool shouldSpawn = !gameStarted;
                    
                    if (gameStarted)
                    {
                        // Check if the player list has changed
                        HashSet<string> newPlayerSet = new HashSet<string>(playerIds);
                        HashSet<string> currentPlayerSet = new HashSet<string>(activePlayers.Keys);
                        
                        if (!newPlayerSet.SetEquals(currentPlayerSet))
                        {
                            Debug.Log("Player list has changed, respawning players");
                            shouldSpawn = true;
                        }
                        else
                        {
                            Debug.Log("Same players, no need to respawn");
                        }
                    }
                    
                    if (shouldSpawn)
                    {
                        SpawnPlayers(playerIds);
                    }
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

        // After processing any network messages that might affect player spawn
        EnsureLocalPlayerExists();
    }
    
    private void HandleGameData(string fromClient, string jsonData)
    {
        // Ignore our own data
        if (fromClient == localPlayerId) return;
        
        try
        {
            // Add debugging to see what's being received
            Debug.Log($"Received game data from {fromClient}: {jsonData.Substring(0, Mathf.Min(50, jsonData.Length))}...");
            
            string[] parts = jsonData.Split('|', 2);
            if (parts.Length < 2) 
            {
                Debug.LogWarning($"Invalid game data format from {fromClient}: {jsonData}");
                return;
            }
            
            string dataType = parts[0];
            string data = parts[1];
            
            switch (dataType)
            {
                case "STATE":
                    // Handle full state update
                    Debug.Log($"Processing STATE data from {fromClient}");
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
                    
                default:
                    Debug.LogWarning($"Unknown data type: {dataType} from {fromClient}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing game data from {fromClient}: {ex.Message}\nData: {jsonData}");
        }
    }
    
    private void OptimizePlayerStateSync(string playerId, PlayerStateData stateData)
    {
        // Skip if this is our own data (shouldn't happen but just in case)
        if (playerId == localPlayerId)
            return;
        
        // IMPORTANT: If we get data for a player that doesn't exist, spawn them
        if (!activePlayers.ContainsKey(playerId))
        {
            Debug.Log($"Received state data for unknown player {playerId}. Spawning them now.");
            SpawnPlayer(playerId);
            
            // Since we just spawned them, we need a small delay before applying state
            StartCoroutine(ApplyStateAfterDelay(playerId, stateData, 0.1f));
            return;
        }
        
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

    private IEnumerator ApplyStateAfterDelay(string playerId, PlayerStateData stateData, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (activePlayers.ContainsKey(playerId))
        {
            var player = activePlayers[playerId];
            player.ApplyRemoteState(stateData, true); // Force teleport for first state
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

    private void EnsureLocalPlayerExists()
    {
        if (string.IsNullOrEmpty(localPlayerId))
        {
            Debug.LogWarning("Cannot ensure local player exists - localPlayerId is null");
            return;
        }
        
        bool playerMissing = localPlayerObject == null || 
                             !activePlayers.ContainsKey(localPlayerId) || 
                             activePlayers[localPlayerId] == null ||
                             activePlayers[localPlayerId].gameObject == null;
        
        if (playerMissing)
        {
            Debug.LogWarning("Local player is missing, respawning...");
            
            // Clean up any existing broken references
            if (activePlayers.ContainsKey(localPlayerId))
            {
                activePlayers.Remove(localPlayerId);
            }
            
            // Spawn a new local player
            SpawnPlayer(localPlayerId, 0);
        }
        else if (!activePlayers[localPlayerId].IsLocal)
        {
            // Fix ownership if it's wrong
            Debug.LogWarning("Local player exists but has wrong ownership, fixing...");
            activePlayers[localPlayerId].SetIsLocal(true);
            SetupLocalPlayerCamera(activePlayers[localPlayerId].gameObject);
            localPlayerObject = activePlayers[localPlayerId].gameObject;
        }
    }

    public void RemovePlayer(string playerId)
    {
        if (activePlayers.ContainsKey(playerId))
        {
            Debug.Log($"Removing player {playerId} from active players list");
            
            // Check if this is our local player
            if (playerId == localPlayerId)
            {
                Debug.LogWarning("Local player was removed! Will attempt to respawn if needed.");
                localPlayerObject = null;
            }
            
            activePlayers.Remove(playerId);
        }
    }

    void OnDestroy()
    {
        // Clean up event handlers to prevent memory leaks
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived -= HandleNetworkMessage;
            NetworkManager.Instance.OnGameDataReceived -= HandleGameData;
            NetworkManager.Instance.OnPositionReset -= HandlePositionReset;
        }
        
        Debug.Log("GameManager destroyed, cleaned up event handlers");
    }

    void OnGUI()
    {
        // Add simple UI to show active players
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 14;
        style.fontStyle = FontStyle.Bold;
        
        // Draw a background box
        GUI.Box(new Rect(10, 10, 300, 25 + activePlayers.Count * 25), "");
        
        // Draw title
        GUI.Label(new Rect(15, 15, 290, 20), "Active Players:", style);
        
        // List all active players
        int i = 0;
        foreach (var player in activePlayers)
        {
            string status = player.Value != null ? "Active" : "NULL";
            string localTag = player.Key == localPlayerId ? " (LOCAL)" : "";
            string posInfo = player.Value != null ? $" at {player.Value.transform.position}" : "";
            
            GUI.Label(new Rect(15, 40 + i * 25, 290, 20), 
                $"{player.Key}{localTag}: {status}{posInfo}", style);
            i++;
        }
    }

    void OnDrawGizmos()
    {
        // Only draw in play mode
        if (!Application.isPlaying) return;
        
        // Draw spheres at player positions for better visibility
        foreach (var player in activePlayers)
        {
            if (player.Value != null)
            {
                // Draw a colored sphere above each player
                Gizmos.color = player.Key == localPlayerId ? Color.blue : Color.red;
                Gizmos.DrawSphere(player.Value.transform.position + Vector3.up * 3f, 1f);
                
                // Draw player ID as text
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(player.Value.transform.position + Vector3.up * 4f, player.Key);
            }
        }
    }
}