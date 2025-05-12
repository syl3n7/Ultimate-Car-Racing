using System.Collections;
using System.Collections.Generic;
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
                carController.Initialize(localPlayerId, true);
                activePlayers.Add(localPlayerId, carController);
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
            
            controller.Respawn(spawnPosition, spawnRotation);
        }
    }
}
