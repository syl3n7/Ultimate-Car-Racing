using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Player Setup")]
    public List<GameObject> playerCarPrefabs = new List<GameObject>();
    public static int SelectedCarIndex = 0;
    public static int SelectedTrackIndex = 0;
    
    public Transform[] spawnPoints;
    public float respawnHeight = 2f; // Height above spawn point
    
    private Dictionary<string, CarController> activePlayers = new Dictionary<string, CarController>();
    private string localPlayerId;
    private GameObject localPlayerObject;
    
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
    
    public void SpawnLocalPlayer()
    {
        // Generate a unique player ID
        localPlayerId = System.Guid.NewGuid().ToString();
        
        // Choose spawn point
        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;
        
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform spawnPoint = spawnPoints[0];
            spawnPosition = spawnPoint.position + Vector3.up * respawnHeight;
            spawnRotation = spawnPoint.rotation;
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
                // Add Initialize method to CarController
                InitializeCarController(carController, localPlayerId, true);
                activePlayers.Add(localPlayerId, carController);
            }
        }
    }
    
    // New method to initialize your simplified CarController
    private void InitializeCarController(CarController controller, string playerId, bool isLocal)
    {
        // Add player input component if this is the local player
        if (isLocal)
        {
            // Ensure car has the right input setup
            PlayerInput playerInput = controller.gameObject.GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                playerInput = controller.gameObject.AddComponent<PlayerInput>();
                playerInput.actions = Resources.Load<InputActionAsset>("PlayerControls");
                playerInput.defaultActionMap = "Player";
            }
        }
        else
        {
            // Disable input for non-local players
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
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform spawnPoint = spawnPoints[0];
                spawnPosition = spawnPoint.position + Vector3.up * respawnHeight;
                spawnRotation = spawnPoint.rotation;
            }
            
            // Add the Respawn method to CarController
            RespawnCarController(controller, spawnPosition, spawnRotation);
        }
    }
    
    // New method to respawn the simplified CarController
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
    
    // Method to get player state for network synchronization
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
    
    // Method to apply remote player state
    public void ApplyPlayerState(PlayerStateData stateData, bool teleport = false)
    {
        if (activePlayers.TryGetValue(stateData.playerId, out CarController controller))
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
    
    // Method to get player input for network synchronization
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
    
    // Method to apply remote player input
    public void ApplyPlayerInput(PlayerInputData inputData)
    {
        if (activePlayers.TryGetValue(inputData.playerId, out CarController controller))
        {
            // Set the input values directly
            controller.moveInput = new Vector2(
                inputData.steering,
                inputData.throttle - inputData.brake // Combine throttle and brake into y axis
            );
        }
    }
}
