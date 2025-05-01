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
        
        // We're ready to start
        SendReadyMessage();
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
            Velocity = playerCar.Rigidbody.velocity,
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
        Vector3 spawnPosition = spawnPoints[spawnIndex].position + Vector3.up * respawnHeight;
        Quaternion spawnRotation = spawnPoints[spawnIndex].rotation;
        
        // Instantiate the player car
        GameObject playerObj = Instantiate(playerCarPrefab, spawnPosition, spawnRotation);
        PlayerController playerController = playerObj.GetComponent<PlayerController>();
        
        // Setup the player controller
        playerController.Initialize(playerId, playerId == localPlayerId);
        
        // Add to active players
        activePlayers[playerId] = playerController;
        
        Debug.Log($"Spawned player {playerId} at spawn point {spawnIndex}");
    }
    
    private void HandleNetworkMessage(string fromClient, string message)
    {
        string[] parts = message.Split('|');
        if (parts.Length < 1) return;
        
        string command = parts[0];
        
        switch (command)
        {
            case "PLAYER_READY":
                // Host handles player ready messages
                if (NetworkManager.Instance.IsHost)
                {
                    CheckAllPlayersReady();
                }
                break;
                
            case "START_GAME":
                // Game starting, spawn players
                string[] playerIds = JsonConvert.DeserializeObject<string[]>(parts[1]);
                SpawnPlayers(playerIds);
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
        
        string[] parts = jsonData.Split('|', 2);
        if (parts.Length < 2) return;
        
        string dataType = parts[0];
        string data = parts[1];
        
        switch (dataType)
        {
            case "STATE":
                // Handle full state update
                HandlePlayerState(fromClient, data);
                break;
                
            case "INPUT":
                // Handle input update (for prediction)
                HandlePlayerInput(fromClient, data);
                break;
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
        if (!NetworkManager.Instance.IsHost) return;
        
        // In a real implementation, you would check if all connected players are ready
        // For simplicity, we'll just start the game after a short delay
        
        StartCoroutine(StartGameAfterDelay(2f));
    }
    
    private IEnumerator StartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Get all player IDs from NetworkManager
        List<string> playerIds = new List<string>();
        
        // Add the host (local player)
        playerIds.Add(localPlayerId);
        
        // Add all connected players
        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            playerIds.Add(player.clientId);
        }
        
        // Serialize the player list
        string playersJson = JsonConvert.SerializeObject(playerIds.ToArray());
        
        // Send start game message to all players
        NetworkManager.Instance.SendMessageToRoom($"START_GAME|{playersJson}");
        
        // Start the game locally
        SpawnPlayers(playerIds.ToArray());
    }
    
    private void RespawnPlayer(string playerId)
    {
        if (!activePlayers.ContainsKey(playerId)) return;
        
        // Find a random spawn point
        int spawnIndex = Random.Range(0, spawnPoints.Length);
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
}