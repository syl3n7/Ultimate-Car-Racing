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
    public GameObject remoteCarPrefab; // Add reference to the custom remote car prefab
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
    
    // Network reference - uses SecureNetworkManager only
    private SecureNetworkManager NetworkManager => SecureNetworkManager.Instance;
    
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
        
        // Restore cursor when going back to menu
        RestoreCursorState();
        
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
    
    // Restore cursor to visible and unlocked
    private void RestoreCursorState()
    {
        // Find any CameraFollow components and restore their cursor state
        CameraFollow[] cameraFollowers = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
        if (cameraFollowers.Length > 0)
        {
            foreach (CameraFollow cameraFollow in cameraFollowers)
            {
                cameraFollow.RestoreCursor();
            }
        }
        else
        {
            // If no CameraFollow component found, restore cursor directly
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
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
    public IEnumerator BroadcastSceneReady()
    {
        yield return new WaitForSeconds(SCENE_LOAD_GRACE_PERIOD);
        
        // Mark scene as fully loaded
        sceneFullyLoaded = true;
        
        // Broadcast "scene ready" message to other players
        if (NetworkManager != null && isMultiplayerGame)
        {
            string currentRoomId = NetworkManager.GetCurrentRoomId();
            
            if (!string.IsNullOrEmpty(currentRoomId))
            {
                Dictionary<string, object> getPlayersMsg = new Dictionary<string, object>
                {
                    { "command", "GET_ROOM_PLAYERS" },
                    { "roomId", currentRoomId }
                };
                
                _ = NetworkManager.SendTcpMessage(getPlayersMsg);
                yield return new WaitForSeconds(0.5f);
                
                Dictionary<string, object> readyMessage = new Dictionary<string, object>
                {
                    { "command", "RELAY_MESSAGE" },
                    { "targetId", NetworkManager.GetRoomHostId() },
                    { "message", $"SCENE_READY:{localPlayerId}" }
                };
                
                _ = NetworkManager.SendTcpMessage(readyMessage);
                Debug.Log($"Sent SCENE_READY message to room host");
            }
            else
            {
                Debug.LogWarning("Cannot send SCENE_READY message - not in a room");
            }
        }
    }

    // In Update method - update to use NetworkManager
    void Update()
    {
        if (isMultiplayerGame && NetworkManager != null && NetworkManager.IsConnected() && sceneFullyLoaded)
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
            if (stateData != null && NetworkManager != null)
            {
                NetworkManager.SendPlayerState(stateData);
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
            if (stateData != null && NetworkManager != null)
            {
                NetworkManager.SendPlayerState(stateData);
                Debug.Log($"Sent state update to newly ready player {playerId}");
            }
        }
    }
    private void SyncPlayerInput()
    {
        if (localPlayerId != null && activePlayers.ContainsKey(localPlayerId))
        {
            var inputData = GetPlayerInput(localPlayerId);
            if (inputData != null && NetworkManager != null)
            {
                NetworkManager.SendPlayerInput(inputData);
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
        if (isMultiplayerGame && NetworkManager != null)
        {
            localPlayerId = NetworkManager.GetClientId();
            
            // Use multiplayer spawn position - ADD RESPAWN HEIGHT to Y value
            Vector3 spawnPosition = new Vector3(
                multiplayerSpawnPosition.x,
                multiplayerSpawnPosition.y + respawnHeight, // Add the respawn height
                multiplayerSpawnPosition.z
            );
            Debug.Log($"Spawning LOCAL PLAYER car at position: {spawnPosition}, original position was {multiplayerSpawnPosition}");
            
            Quaternion spawnRotation = Quaternion.identity;
            
            // Store the player's garage index
            if (!playerGarageIndices.ContainsKey(localPlayerId))
            {
                playerGarageIndices[localPlayerId] = multiplayerSpawnIndex;
            }
            
            // First check if player already exists to avoid dictionary key collision
            if (activePlayers.ContainsKey(localPlayerId))
            {
                Debug.Log($"Player with ID {localPlayerId} already exists. Removing before respawning.");
                CarController existingController = activePlayers[localPlayerId];
                if (existingController != null && existingController.gameObject != null)
                {
                    Destroy(existingController.gameObject);
                }
                activePlayers.Remove(localPlayerId);
            }
            
            // Instantiate car prefab
            if (playerCarPrefabs.Count > 0)
            {
                int carIndex = Mathf.Clamp(SelectedCarIndex, 0, playerCarPrefabs.Count - 1);
                localPlayerObject = Instantiate(playerCarPrefabs[carIndex], spawnPosition, spawnRotation);
                
                // Properly tag and name it for easy identification
                localPlayerObject.tag = "Player";
                localPlayerObject.name = $"LocalPlayer_{localPlayerId}";
                
                // Initialize car controller
                CarController carController = localPlayerObject.GetComponent<CarController>();
                if (carController != null)
                {
                    InitializeCarController(carController, localPlayerId, true);
                    activePlayers.Add(localPlayerId, carController);
                    
                    // Force camera to follow this player
                    SetupCameraForLocalPlayer(localPlayerObject.transform);
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
            
            // Check for existing player
            if (activePlayers.ContainsKey(localPlayerId))
            {
                Debug.Log($"Player with ID {localPlayerId} already exists. Removing before respawning.");
                CarController existingController = activePlayers[localPlayerId];
                if (existingController != null && existingController.gameObject != null)
                {
                    Destroy(existingController.gameObject);
                }
                activePlayers.Remove(localPlayerId);
            }
            
            // Instantiate car prefab
            if (playerCarPrefabs.Count > 0)
            {
                int carIndex = Mathf.Clamp(SelectedCarIndex, 0, playerCarPrefabs.Count - 1);
                localPlayerObject = Instantiate(playerCarPrefabs[carIndex], spawnPosition, spawnRotation);
                
                // Properly tag and name it
                localPlayerObject.tag = "Player";
                localPlayerObject.name = $"LocalPlayer_{localPlayerId}";
                
                // Initialize car controller
                CarController carController = localPlayerObject.GetComponent<CarController>();
                if (carController != null)
                {
                    InitializeCarController(carController, localPlayerId, true);
                    activePlayers.Add(localPlayerId, carController);
                    
                    // Force camera to follow this player
                    SetupCameraForLocalPlayer(localPlayerObject.transform);
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
            // Check if we have the custom remote car prefab
            if (remoteCarPrefab != null)
            {
                // Adjust position to prevent being stuck in ground
                Vector3 spawnPosition = new Vector3(
                    position.x,
                    position.y + respawnHeight,
                    position.z
                );
                
                // Spawn the remote car using the custom prefab
                GameObject carObject = Instantiate(remoteCarPrefab, spawnPosition, rotation);
                carObject.name = $"RemotePlayer_{playerId}";
                
                // Get or add the RemotePlayerController component
                RemotePlayerController remoteController = carObject.GetComponent<RemotePlayerController>();
                if (remoteController != null)
                {
                    remoteController.SetPlayerId(playerId);
                }
                else
                {
                    remoteController = carObject.AddComponent<RemotePlayerController>();
                    remoteController.SetPlayerId(playerId);
                    Debug.Log("Added RemotePlayerController to remote car prefab");
                }
                
                // Also add or get CarController for compatibility
                CarController carController = carObject.GetComponent<CarController>();
                if (carController == null)
                {
                    carController = carObject.AddComponent<CarController>();
                    Debug.Log("Added CarController to remote car prefab");
                }
                
                // Initialize the remote car
                InitializeCarController(carController, playerId, false);
                activePlayers[playerId] = carController;
                Debug.Log($"SUCCESS - Remote player {playerId} added to active players dictionary using custom prefab");
            }
            // Fallback to the original method if custom prefab is not assigned
            else if (playerCarPrefabs != null && playerCarPrefabs.Count > 0)
            {
                Debug.LogWarning("Custom remote car prefab not assigned! Using fallback method.");
                
                // Use the second car model if available
                int carIndex = Mathf.Min(1, playerCarPrefabs.Count - 1);
                
                // Adjust position to prevent being stuck in ground
                Vector3 spawnPosition = new Vector3(
                    position.x,
                    position.y + respawnHeight,
                    position.z
                );
                
                // Create a display-only version of the car for remote players
                GameObject carObject = CreateDisplayOnlyCar(playerCarPrefabs[carIndex], spawnPosition, rotation, playerId);
                
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
            else
            {
                Debug.LogError("Cannot spawn remote player: No car prefabs available!");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error spawning remote player: {e.Message}\n{e.StackTrace}");
        }
    }
    
    // Create a display-only version of the car prefab that won't interfere with player input
    private GameObject CreateDisplayOnlyCar(GameObject originalPrefab, Vector3 position, Quaternion rotation, string playerId)
    {
        // Instantiate the car
        GameObject carObject = Instantiate(originalPrefab, position, rotation);
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
        
        // Disable components that might interfere with input
        DisableInputComponents(carObject);
        
        return carObject;
    }
    
    // Disable any components that might capture or interfere with input
    private void DisableInputComponents(GameObject carObject)
    {
        // Disable PlayerInput component if it exists
        PlayerInput playerInput = carObject.GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
        }
        
        // Disable any input-related script components
        var inputComponents = carObject.GetComponentsInChildren<MonoBehaviour>();
        foreach (var component in inputComponents)
        {
            // Disable any components with "Input" in the name except the essential ones
            if (component.GetType().Name.Contains("Input") && 
                component.GetType().Name != "CarController" && 
                component.GetType().Name != "Rigidbody")
            {
                component.enabled = false;
            }
        }
        
        // Add a tag to identify this as a remote car
        carObject.tag = "RemotePlayer";
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
            controller.gameObject.tag = "Player";
            
            // Add debug info to help troubleshoot
            Debug.Log($"*** LOCAL PLAYER CAR INITIALIZED: {controller.gameObject.name} with ID {playerId} ***");
            
            // Setup camera follow
            CameraFollow cameraFollow = FindFirstObjectByType<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.target = controller.transform;
                Debug.Log($"Camera now following local player: {controller.gameObject.name}");
            }
            
            // Make sure controls are enabled
            controller.EnableControls(true);
        }
        else
        {
            // Setup for remote car - important differences
            controller.isLocalPlayer = false;
            controller.gameObject.tag = "RemotePlayer";
            
            // Add debug info
            Debug.Log($"REMOTE PLAYER CAR INITIALIZED: {controller.gameObject.name} with ID {playerId}");
            
            // Disable local controls
            controller.EnableControls(false);
            
            // Make sure physics simulation is active but less performance-intensive
            Rigidbody rb = controller.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.sleepThreshold = 0.0f; // Never sleep
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // Use simpler collision detection
                rb.interpolation = RigidbodyInterpolation.None; // Disable interpolation to save performance
            }
            
            // Modify the car's layer to prevent input interference
            controller.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
    }
    
    // Set up camera to follow local player
    private void SetupCameraForLocalPlayer(Transform playerTransform)
    {
        Debug.Log($"Setting up camera to follow local player: {playerTransform.name}");
        
        // Find all CameraFollow scripts in the scene
        CameraFollow[] cameraFollowers = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
        
        if (cameraFollowers.Length > 0)
        {
            foreach (CameraFollow cam in cameraFollowers)
            {
                cam.SetTarget(playerTransform);
                Debug.Log($"Camera {cam.name} now following {playerTransform.name}");
            }
        }
        else
        {
            // If no camera follow script found, try to find main camera and add one
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                CameraFollow cameraFollow = mainCamera.GetComponent<CameraFollow>();
                if (cameraFollow == null)
                {
                    cameraFollow = mainCamera.gameObject.AddComponent<CameraFollow>();
                    Debug.Log("Added CameraFollow component to main camera");
                }
                
                cameraFollow.SetTarget(playerTransform);
                Debug.Log($"Camera now following {playerTransform.name}");
            }
            else
            {
                Debug.LogError("No main camera found in the scene!");
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

            // Check if this is a remote player with RemotePlayerController
            RemotePlayerController remoteController = controller.gameObject.GetComponent<RemotePlayerController>();
            if (remoteController != null)
            {
                // Use the specialized RemotePlayerController for interpolation
                remoteController.UpdatePosition(stateData.position, stateData.rotation, stateData.velocity, stateData.angularVelocity);
            }
            else
            {
                // Fallback to the original method if no RemotePlayerController is found
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
                        // Simple position lerping as fallback
                        controller.transform.position = Vector3.Lerp(controller.transform.position, stateData.position, 0.25f);
                        controller.transform.rotation = Quaternion.Slerp(controller.transform.rotation, stateData.rotation, 0.25f);
                        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, stateData.velocity, 0.25f);
                        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, stateData.angularVelocity, 0.25f);
                    }
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
