using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Player Setup")]
    public List<GameObject> playerCarPrefabs = new List<GameObject>();
    public static int SelectedCarIndex = 0;
    public static int SelectedTrackIndex = 0;
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>(); 
    private bool sceneFullyLoaded = false;
    private float sceneLoadTime = 3f;
    private const float SCENE_LOAD_GRACE_PERIOD = 7f; // Wait 7 seconds after scene loads

    [System.Serializable]
    public class SpawnPointData
    {
        public Vector3 position;
        public Vector3 rotation; // Euler angles for inspector
        
        public Quaternion GetRotation()
        {
            return Quaternion.Euler(rotation);
        }
    }
    
    private readonly Vector3[] trackGaragePositions = new Vector3[]
    {
        new Vector3(66, -2, 0.8f),   // Position 0
        new Vector3(60, -2, 0.8f),   // Position 1
        new Vector3(54, -2, 0.8f),   // Position 2
        new Vector3(47, -2, 0.8f),   // Position 3
        new Vector3(41, -2, 0.8f),   // Position 4
        new Vector3(35, -2, 0.8f),   // Position 5
        new Vector3(28, -2, 0.8f),   // Position 6
        new Vector3(22, -2, 0.8f),   // Position 7
        new Vector3(16, -2, 0.8f),   // Position 8
        new Vector3(9, -2, 0.8f),    // Position 9
        new Vector3(3, -2, 0.8f),    // Position 10
        new Vector3(-3, -2, 0.8f),   // Position 11
        new Vector3(-9, -2, 0.8f),   // Position 12
        new Vector3(-15, -2, 0.8f),  // Position 13
        new Vector3(-22, -2, 0.8f),  // Position 14
        new Vector3(-28, -2, 0.8f),  // Position 15
        new Vector3(-34, -2, 0.8f),  // Position 16
        new Vector3(-41, -2, 0.8f),  // Position 17
        new Vector3(-47, -2, 0.8f),  // Position 18
        new Vector3(-54, -2, 0.8f)   // Position 19
    };

    // Add a dictionary to map players to their garage index
    private Dictionary<string, int> playerGarageIndices = new Dictionary<string, int>();
    private int multiplayerSpawnIndex = 0;
    public float respawnHeight = 2f; // Height above spawn point
    
    [Header("Multiplayer Settings")]
    public float syncInterval = 0.1f;
    public float inputSyncInterval = 0.05f;
    
    private Dictionary<string, CarController> activePlayers = new Dictionary<string, CarController>();
    private string localPlayerId;
    private GameObject localPlayerObject;
    private bool isMultiplayerGame = false;
    private Vector3 multiplayerSpawnPosition;
    private float lastStateSyncTime = 0f;
    private float lastInputSyncTime = 0f;
    
    // For synchronization over network
    [Serializable]
    public class PlayerStateData
    {
        public string playerId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float timestamp;
    }
    
    [Serializable]
    public class PlayerInputData
    {
        public string playerId;
        public float steering;
        public float throttle;
        public float brake;
        public float timestamp;
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("GameManager initialized as singleton");
        }
        else if (Instance != this)
        {
            Debug.Log("Duplicate GameManager destroyed");
            Destroy(gameObject);
            return;
        }
        
        // Register for scene loading events to properly handle scene transitions
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    public void LoadRaceScene(int trackIndex)
    {
        SelectedTrackIndex = trackIndex;
        string sceneName = $"RaceTrack{trackIndex}";
        Debug.Log($"Loading race scene: {sceneName}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    public void LoadMainMenu()
    {
        Debug.Log("Loading main menu scene");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        string sceneName = scene.name;
        Debug.Log($"GameManager: Scene loaded: {sceneName}");
        
        // Reset scene loaded flag
        sceneFullyLoaded = false;
        sceneLoadTime = Time.time;
        
        // Reset multiplayer flag when returning to menu scenes
        if (sceneName.Contains("Menu") || sceneName == "MainMenu")
        {
            // Reset state for menu scenes
            if (activePlayers.Count > 0)
            {
                foreach (var player in activePlayers.Values)
                {
                    if (player != null && player.gameObject != null)
                    {
                        Destroy(player.gameObject);
                    }
                }
                activePlayers.Clear();
            }
        }
        else if (sceneName.Contains("Track") || sceneName.Contains("Race"))
        {
            // Automatically spawn player in race scenes
            Debug.Log("Race scene detected - spawning player");
            SpawnLocalPlayer();
            
            // Broadcast scene ready message after grace period
            StartCoroutine(BroadcastSceneReady());
        }
    }
    
    // In BroadcastSceneReady method - update to use NetworkManager
    private IEnumerator BroadcastSceneReady()
    {
        // Wait for grace period to ensure scene is fully loaded
        yield return new WaitForSeconds(SCENE_LOAD_GRACE_PERIOD);
        
        // Mark scene as fully loaded
        sceneFullyLoaded = true;
        
        // Broadcast "scene ready" message to all players
        if (NetworkManager.Instance != null && isMultiplayerGame)
        {
            Dictionary<string, object> readyMessage = new Dictionary<string, object>
            {
                { "type", "RELAY_MESSAGE" },
                { "room_id", NetworkManager.Instance.GetCurrentRoomId() },
                { "message", new Dictionary<string, object> {
                    { "type", "SCENE_READY" },
                    { "player_id", localPlayerId }
                }}
            };
            
            _ = NetworkManager.Instance.SendTcpMessage(readyMessage);
            Debug.Log("Sent SCENE_READY message to all players");
        }
    }

    // In Update method - update to use NetworkManager
    void Update()
    {
        if (isMultiplayerGame && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected() && sceneFullyLoaded)
        {
            // Only sync state when scene is fully loaded
            if (Time.time - lastStateSyncTime > syncInterval)
            {
                SyncPlayerState();
                lastStateSyncTime = Time.time;
            }
            
            // Only sync input when scene is fully loaded
            if (Time.time - lastInputSyncTime > inputSyncInterval)
            {
                SyncPlayerInput();
                lastInputSyncTime = Time.time;
            }
        }
    }
    

    // Update SetMultiplayerSpawnPosition to accept both position and index
    public void SetMultiplayerSpawnPosition(Vector3 position, int spawnIndex = 0)
    {
        isMultiplayerGame = true;
        multiplayerSpawnPosition = position;
        multiplayerSpawnIndex = spawnIndex;
        
        // Log the assigned garage for debugging
        Debug.Log($"Local player assigned to garage {spawnIndex + 1} at position {position}");
    }
    private void SyncPlayerState()
    {
        if (localPlayerId != null && activePlayers.ContainsKey(localPlayerId))
        {
            var stateData = GetPlayerState(localPlayerId);
            if (stateData != null && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.SendPlayerState(stateData);
            }
        }
    }
    
    private bool IsLocalPlayerActive()
    {
        return !string.IsNullOrEmpty(localPlayerId) && 
            activePlayers.ContainsKey(localPlayerId) && 
            activePlayers[localPlayerId] != null;
    }

    // In HandlePlayerReady method - update to use NetworkManager
    public void HandlePlayerReady(string playerId)
    {
        Debug.Log($"Player {playerId} is ready - forcing state sync");

        if (playerId != localPlayerId && IsLocalPlayerActive())
        {
            // Send our state to the newly ready player
            var stateData = GetPlayerState(localPlayerId);
            if (stateData != null && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.SendPlayerState(stateData);
                Debug.Log($"Sent state update to newly ready player {playerId}");
            }
        }
    }
    private void SyncPlayerInput()
    {
        if (localPlayerId != null && activePlayers.ContainsKey(localPlayerId))
        {
            var inputData = GetPlayerInput(localPlayerId);
            if (inputData != null && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.SendPlayerInput(inputData);
            }
        }
    }
    
    public void SetMultiplayerSpawnPosition(Vector3 position)
    {
        isMultiplayerGame = true;
        multiplayerSpawnPosition = position;
    }
    
    public void SpawnLocalPlayer()
    {
        // Check if we're in a menu scene
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentSceneName.Contains("Menu") || currentSceneName.Contains("Lobby"))
        {
            Debug.Log("Not spawning player in menu/lobby scene");
            return;
        }
        // Check if we're in a multiplayer game
        if (isMultiplayerGame && NetworkManager.Instance != null)
        {
            localPlayerId = NetworkManager.Instance.GetClientId();
            
            // Use multiplayer spawn position - ADD RESPAWN HEIGHT to Y value
            Vector3 spawnPosition = new Vector3(
                multiplayerSpawnPosition.x,
                multiplayerSpawnPosition.y + respawnHeight, // Add the respawn height
                multiplayerSpawnPosition.z
            );
            Debug.Log($"Spawning car at position: {spawnPosition}, original position was {multiplayerSpawnPosition}");
            
            Quaternion spawnRotation = Quaternion.identity;
            
            // Store the player's garage index
            if (!playerGarageIndices.ContainsKey(localPlayerId))
            {
                playerGarageIndices[localPlayerId] = multiplayerSpawnIndex;
            }
            
            // Instantiate car prefab
            if (playerCarPrefabs.Count > 0)
            {
                int carIndex = Mathf.Clamp(SelectedCarIndex, 0, playerCarPrefabs.Count - 1);
                localPlayerObject = Instantiate(playerCarPrefabs[carIndex], spawnPosition, spawnRotation);
                
                // Initialize car controller
                CarController carController = localPlayerObject.GetComponent<CarController>();
                if (carController != null)
                {
                    InitializeCarController(carController, localPlayerId, true);
                    activePlayers.Add(localPlayerId, carController);
                }
            }
        }
        else
        {
            // Single player spawn logic
            localPlayerId = System.Guid.NewGuid().ToString();
            
            // Choose spawn point for single player (use first garage position)
            Vector3 spawnPosition = trackGaragePositions.Length > 0 ? 
                                trackGaragePositions[0] + Vector3.up * respawnHeight : 
                                new Vector3(0, 5, 0);
            Quaternion spawnRotation = Quaternion.identity;
            
            // Instantiate car prefab
            if (playerCarPrefabs.Count > 0)
            {
                int carIndex = Mathf.Clamp(SelectedCarIndex, 0, playerCarPrefabs.Count - 1);
                localPlayerObject = Instantiate(playerCarPrefabs[carIndex], spawnPosition, spawnRotation);
                
                // Initialize car controller
                CarController carController = localPlayerObject.GetComponent<CarController>();
                if (carController != null)
                {
                    InitializeCarController(carController, localPlayerId, true);
                    activePlayers.Add(localPlayerId, carController);
                }
            }
        }
    }
    
    public void SpawnRemotePlayer(string playerId, Vector3 position, Quaternion rotation)
    {
        Debug.Log($"*** SPAWNING REMOTE PLAYER: {playerId} at {position} ***");
        
        if (playerId == localPlayerId)
        {
            Debug.LogWarning($"Not spawning remote player because it's the local player: {playerId}");
            return;
        }
        
        // Remove any existing player with this ID first
        if (activePlayers.ContainsKey(playerId))
        {
            var existingController = activePlayers[playerId];
            if (existingController != null && existingController.gameObject != null)
            {
                Debug.Log($"Destroying existing player object for {playerId}");
                Destroy(existingController.gameObject);
            }
            activePlayers.Remove(playerId);
        }
        
        // Force-create a new player (always refresh)
        try 
        {
            // Make sure we have prefabs
            if (playerCarPrefabs == null || playerCarPrefabs.Count == 0)
            {
                Debug.LogError("Cannot spawn remote player: No car prefabs available!");
                return;
            }
            
            // Use the second car model if available
            int carIndex = Mathf.Min(1, playerCarPrefabs.Count - 1);
            
            // Adjust position to prevent being stuck in ground
            Vector3 spawnPosition = new Vector3(
                position.x,
                position.y + respawnHeight,
                position.z
            );
            
            // Instantiate the car
            GameObject carObject = Instantiate(playerCarPrefabs[carIndex], spawnPosition, rotation);
            carObject.name = $"RemotePlayer_{playerId}";
            
            // Make it red to distinguish from local car
            Renderer[] renderers = carObject.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                foreach (Material mat in r.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        mat.color = Color.red;
                    }
                }
            }
            
            // Set up the car controller
            CarController carController = carObject.GetComponent<CarController>();
            if (carController != null)
            {
                InitializeCarController(carController, playerId, false);
                activePlayers[playerId] = carController;
                Debug.Log($"SUCCESS - Remote player {playerId} added to active players dictionary");
            }
            else
            {
                Debug.LogError($"Car controller not found on car prefab for player {playerId}!");
                Destroy(carObject);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error spawning remote player: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Initialize the car controller
    private void InitializeCarController(CarController controller, string playerId, bool isLocal)
    {
        // Tag the car with the player ID
        controller.playerId = playerId;
        
        if (isLocal)
        {
            // Setup for local car
            controller.isLocalPlayer = true;
            
            // Setup camera follow
            CameraFollow cameraFollow = FindObjectOfType<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.target = controller.transform;
            }
            
            // Make sure controls are enabled
            controller.EnableControls(true);
        }
        else
        {
            // Setup for remote car - important differences
            controller.isLocalPlayer = false;
            
            // Disable local controls
            controller.EnableControls(false);
            
            // Make sure physics simulation is active
            Rigidbody rb = controller.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.sleepThreshold = 0.0f; // Never sleep
            }
        }
    }
    
    public void RespawnPlayer(string playerId)
    {
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            Vector3 spawnPosition;
            Quaternion spawnRotation = Quaternion.identity;

            if (isMultiplayerGame && playerId == localPlayerId)
            {
                // Use the multiplayer spawn position from the assigned garage
                // Add respawnHeight to Y
                spawnPosition = new Vector3(
                    multiplayerSpawnPosition.x,
                    multiplayerSpawnPosition.y + respawnHeight,
                    multiplayerSpawnPosition.z
                );

                // Save this player's garage index
                if (!playerGarageIndices.ContainsKey(playerId))
                {
                    playerGarageIndices[playerId] = multiplayerSpawnIndex;
                }
            }
            else if (playerGarageIndices.TryGetValue(playerId, out int garageIndex))
            {
                // Use the player's assigned garage if we know it
                if (garageIndex >= 0 && garageIndex < trackGaragePositions.Length)
                {
                    spawnPosition = trackGaragePositions[garageIndex];
                }
                else
                {
                    // Fallback to default
                    spawnPosition = new Vector3(0, 5, 0);
                }
            }
            else if (trackGaragePositions.Length > 0)
            {
                // Use the first garage position as default
                spawnPosition = trackGaragePositions[0] + Vector3.up * respawnHeight;
            }
            else
            {
                // Last resort default position
                spawnPosition = new Vector3(0, 5, 0);
            }

            RespawnCarController(controller, spawnPosition, spawnRotation);
        }
    }
    
    // Respawn the car controller
    private void RespawnCarController(CarController controller, Vector3 position, Quaternion rotation)
    {
        // Reset the car's position and rotation
        Rigidbody rb = controller.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        controller.transform.position = position;
        controller.transform.rotation = rotation;
    }
    
    // Get player state for network synchronization
    public PlayerStateData GetPlayerState(string playerId)
    {
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            Rigidbody rb = controller.GetComponent<Rigidbody>();
            if (rb != null)
            {
                return new PlayerStateData
                {
                    playerId = playerId,
                    position = controller.transform.position,
                    rotation = controller.transform.rotation,
                    velocity = rb.linearVelocity,
                    angularVelocity = rb.angularVelocity,
                    timestamp = Time.time
                };
            }
        }
        return null;
    }
    
    // Helper method to check if a player is already active
    public bool IsPlayerActive(string playerId)
    {
        // Check if the player exists in the active players dictionary
        if (string.IsNullOrEmpty(playerId))
        return false;
        
        return activePlayers.ContainsKey(playerId) && 
        activePlayers[playerId] != null && 
        activePlayers[playerId].gameObject != null;
    }

    // Apply remote player state
    public void ApplyPlayerState(PlayerStateData stateData, bool teleport = false)
    {
        string playerId = stateData.playerId;
        Debug.Log($"ApplyPlayerState called for player {playerId} at position {stateData.position}, teleport={teleport}");
        
        // If we don't have this player yet, spawn them
        if (!activePlayers.ContainsKey(playerId) || activePlayers[playerId] == null || activePlayers[playerId].gameObject == null)
        {
            Debug.Log($"URGENT: Player {playerId} not in activePlayers list or is invalid - forcing spawn");
            SpawnRemotePlayer(playerId, stateData.position, stateData.rotation);
            teleport = true; // Force teleport for newly spawned players
        }
        
        // Try again after potential spawn
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            // Double-check if the controller is still valid
            if (controller == null || controller.gameObject == null)
            {
                Debug.LogWarning($"Controller for player {playerId} was destroyed after checking - respawning again");
                activePlayers.Remove(playerId);
                SpawnRemotePlayer(playerId, stateData.position, stateData.rotation);
                return;
            }
            
            Rigidbody rb = controller.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (teleport)
                {
                    Debug.Log($"Teleporting player {playerId} to {stateData.position}");
                    controller.transform.position = stateData.position;
                    controller.transform.rotation = stateData.rotation;
                    rb.linearVelocity = stateData.velocity;
                    rb.angularVelocity = stateData.angularVelocity;
                }
                else
                {
                    // Smoothly interpolate position and rotation
                    controller.transform.position = Vector3.Lerp(controller.transform.position, stateData.position, 0.25f);
                    controller.transform.rotation = Quaternion.Slerp(controller.transform.rotation, stateData.rotation, 0.25f);
                    rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, stateData.velocity, 0.25f);
                    rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, stateData.angularVelocity, 0.25f);
                }
            }
        }
        else
        {
            Debug.LogError($"Failed to find controller for player {playerId} even after spawn attempt!");
        }
    }
    
    // Get player input for network synchronization
    public PlayerInputData GetPlayerInput(string playerId)
    {
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            return new PlayerInputData
            {
                playerId = playerId,
                steering = controller.moveInput.x,
                throttle = controller.moveInput.y > 0 ? controller.moveInput.y : 0,
                brake = controller.moveInput.y < 0 ? -controller.moveInput.y : 0,
                timestamp = Time.time
            };
        }
        return null;
    }
    
    // Apply remote player input
    public void ApplyPlayerInput(PlayerInputData inputData)
    {
        string playerId = inputData.playerId;
        
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            // Set the input values directly
            controller.moveInput = new Vector2(
                inputData.steering,
                inputData.throttle - inputData.brake // Combine throttle and brake into y axis
            );
        }
    }
    
    public void RemovePlayer(string playerId)
    {
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            Destroy(controller.gameObject);
            activePlayers.Remove(playerId);
        }
    }
}
