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
    
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();
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
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Update()
    {
        if (isMultiplayerGame && NetworkClient.Instance != null && NetworkClient.Instance.IsConnected())
        {
            // Sync state at regular intervals
            if (Time.time - lastStateSyncTime > syncInterval)
            {
                SyncPlayerState();
                lastStateSyncTime = Time.time;
            }
            
            // Sync input at regular intervals or when input changes
            if (Time.time - lastInputSyncTime > inputSyncInterval)
            {
                SyncPlayerInput();
                lastInputSyncTime = Time.time;
            }
        }
    }
    
    private void SyncPlayerState()
    {
        if (localPlayerId != null && activePlayers.ContainsKey(localPlayerId))
        {
            var stateData = GetPlayerState(localPlayerId);
            if (stateData != null && NetworkClient.Instance != null)
            {
                NetworkClient.Instance.SendPlayerState(stateData);
            }
        }
    }
    
    private void SyncPlayerInput()
    {
        if (localPlayerId != null && activePlayers.ContainsKey(localPlayerId))
        {
            var inputData = GetPlayerInput(localPlayerId);
            if (inputData != null && NetworkClient.Instance != null)
            {
                NetworkClient.Instance.SendPlayerInput(inputData);
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
        // Check if we're in a multiplayer game
        if (isMultiplayerGame && NetworkClient.Instance != null)
        {
            localPlayerId = NetworkClient.Instance.GetClientId();
            
            // Use multiplayer spawn position
            Vector3 spawnPosition = multiplayerSpawnPosition;
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
        else
        {
            // Single player spawn logic
            localPlayerId = System.Guid.NewGuid().ToString();
            
            // Choose spawn point
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                SpawnPointData spawnPoint = spawnPoints[0];
                spawnPosition = spawnPoint.position + Vector3.up * respawnHeight;
                spawnRotation = spawnPoint.GetRotation();
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
    }
    
    public void SpawnRemotePlayer(string playerId, Vector3 position, Quaternion rotation)
    {
        if (playerId == localPlayerId || activePlayers.ContainsKey(playerId))
            return;
        
        if (playerCarPrefabs.Count > 0)
        {
            // For simplicity, use the same car prefab for all remote players
            int carIndex = Mathf.Clamp(SelectedCarIndex, 0, playerCarPrefabs.Count - 1);
            GameObject playerObj = Instantiate(playerCarPrefabs[carIndex], position, rotation);
            
            // Initialize car controller
            CarController carController = playerObj.GetComponent<CarController>();
            if (carController != null)
            {
                InitializeCarController(carController, playerId, false);
                activePlayers.Add(playerId, carController);
            }
        }
    }
    
    // Initialize the car controller
    private void InitializeCarController(CarController controller, string playerId, bool isLocal)
    {
        // Add player input component if this is the local player
        if (isLocal)
        {
            // Make sure car has the right input setup
            PlayerInput playerInput = controller.gameObject.GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                playerInput = controller.gameObject.AddComponent<PlayerInput>();
                
                // Try to load the input actions asset from Resources
                InputActionAsset inputActions = Resources.Load<InputActionAsset>("PlayerControls");
                if (inputActions != null)
                {
                    playerInput.actions = inputActions;
                    playerInput.defaultActionMap = "Player";
                }
                else
                {
                    Debug.LogWarning("PlayerControls input actions asset not found in Resources folder.");
                }
            }
        }
        else
        {
            // Disable input for remote players
            PlayerInput playerInput = controller.gameObject.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = false;
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
                // Use the multiplayer spawn position
                spawnPosition = multiplayerSpawnPosition;
            }
            else if (spawnPoints != null && spawnPoints.Count > 0)
            {
                // Use a regular spawn point for single player
                SpawnPointData spawnPoint = spawnPoints[0];
                spawnPosition = spawnPoint.position + Vector3.up * respawnHeight;
                spawnRotation = spawnPoint.GetRotation();
            }
            else
            {
                // Default position if no spawn points available
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
    
    // Apply remote player state
    public void ApplyPlayerState(PlayerStateData stateData, bool teleport = false)
    {
        string playerId = stateData.playerId;
        
        // If we don't have this player yet, spawn them
        if (!activePlayers.ContainsKey(playerId))
        {
            SpawnRemotePlayer(playerId, stateData.position, stateData.rotation);
        }
        
        if (activePlayers.TryGetValue(playerId, out CarController controller))
        {
            Rigidbody rb = controller.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (teleport)
                {
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
