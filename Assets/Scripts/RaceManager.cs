using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltimateCarRacing.Networking;
using System;
using System.Linq;
using Newtonsoft.Json;

public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }
    
    [Header("Race Settings")]
    public int totalLaps = 3;
    public Transform[] checkpoints;
    public float countdownDuration = 3f;
    public float respawnDelay = 3f;
    public float maxVehicleFlipTime = 5f; // Auto-respawn if upside down for this long
    
    [Header("UI References")]
    public GameObject countdownUI;
    public TMPro.TextMeshProUGUI countdownText;
    public GameObject raceCompletedUI;
    public TMPro.TextMeshProUGUI raceResultsText;
    
    // Track player race state
    private Dictionary<string, PlayerRaceState> playerStates = new Dictionary<string, PlayerRaceState>();
    private bool raceStarted = false;
    private float raceStartTime;
    private bool raceCompleted = false;
    
    [System.Serializable]
    public class PlayerRaceState
    {
        public string playerId;
        public int currentLap = 0;
        public int nextCheckpoint = 0;
        public int position = 0;
        public float lastCheckpointTime = 0f;
        public float bestLapTime = float.MaxValue;
        public float lastLapTime = 0f;
        public float totalRaceTime = 0f;
        public bool finished = false;
        public Vector3 lastValidPosition;
        public Quaternion lastValidRotation;
        public float lastFlipCheckTime = 0f;
        public float flipStartTime = 0f;
        public Dictionary<int, float> lapTimes = new Dictionary<int, float>();
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Find checkpoints in the scene if not set
        if (checkpoints == null || checkpoints.Length == 0)
        {
            FindCheckpoints();
        }
    }
    
    void Start()
    {
        // Register for network events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameDataReceived += HandleRaceData;
        }
        
        // Hide race completed UI
        if (raceCompletedUI != null)
        {
            raceCompletedUI.SetActive(false);
        }
        
        // Start race countdown when all players are ready
        StartCoroutine(StartRaceCountdown());
    }
    
    private void FindCheckpoints()
    {
        // Look for a parent object containing checkpoints
        GameObject checkpointsParent = GameObject.Find("Checkpoints");
        
        if (checkpointsParent != null)
        {
            List<Transform> points = new List<Transform>();
            foreach (Transform child in checkpointsParent.transform)
            {
                points.Add(child);
            }
            
            // Sort checkpoints numerically by name if possible
            checkpoints = points.OrderBy(t => {
                if (int.TryParse(t.name.Replace("Checkpoint", "").Trim(), out int num))
                    return num;
                return 0;
            }).ToArray();
            
            Debug.Log($"Found {checkpoints.Length} checkpoints");
        }
        else
        {
            // Look for objects tagged as "Checkpoint"
            GameObject[] taggedCheckpoints = GameObject.FindGameObjectsWithTag("Checkpoint");
            if (taggedCheckpoints.Length > 0)
            {
                checkpoints = new Transform[taggedCheckpoints.Length];
                for (int i = 0; i < taggedCheckpoints.Length; i++)
                {
                    checkpoints[i] = taggedCheckpoints[i].transform;
                }
                Debug.Log($"Found {checkpoints.Length} checkpoints with Checkpoint tag");
            }
            else
            {
                Debug.LogWarning("No checkpoints found in the scene!");
            }
        }
    }
    
    // Start race countdown
    private IEnumerator StartRaceCountdown()
    {
        // Initialize all player states
        InitializePlayerStates();
        
        if (countdownUI != null)
            countdownUI.SetActive(true);
        
        // Disable all car controls during countdown
        DisableAllCarControls();
        
        // Count down from 3
        for (int i = 3; i > 0; i--)
        {
            if (countdownText != null)
                countdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }
        
        // GO!
        if (countdownText != null)
            countdownText.text = "GO!";
        
        // Start the race
        raceStarted = true;
        raceStartTime = Time.time;
        
        // Enable controls for all cars
        EnableAllCarControls();
        
        // Hide countdown after a moment
        yield return new WaitForSeconds(1f);
        if (countdownUI != null)
            countdownUI.SetActive(false);
    }
    
    private void InitializePlayerStates()
    {
        playerStates.Clear();
        
        // Get all player controllers in the scene
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        
        foreach (var player in players)
        {
            PlayerRaceState state = new PlayerRaceState
            {
                playerId = player.PlayerId,
                lastCheckpointTime = Time.time,
                lastValidPosition = player.transform.position,
                lastValidRotation = player.transform.rotation
            };
            
            playerStates[player.PlayerId] = state;
            Debug.Log($"Added player {player.PlayerId} to race");
        }
        
        // Initial position sorting (arbitrary at start)
        UpdateRacePositions();
    }
    
    private void EnableAllCarControls()
    {
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            player.EnableControls();
        }
    }
    
    private void DisableAllCarControls()
    {
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            player.DisableControls();
        }
    }
    
    public int GetPlayerPosition(string playerId)
    {
        if (playerStates.ContainsKey(playerId))
        {
            return playerStates[playerId].position;
        }
        return 0;
    }
    
    public int GetPlayerLap(string playerId)
    {
        if (playerStates.ContainsKey(playerId))
        {
            return playerStates[playerId].currentLap;
        }
        return 0;
    }
    
    void Update()
    {
        if (!raceStarted || raceCompleted) return;
        
        // Update race time for all players
        foreach (var kvp in playerStates)
        {
            if (!kvp.Value.finished)
            {
                kvp.Value.totalRaceTime = Time.time - raceStartTime;
            }
        }
        
        // Check for flipped vehicles and respawn if needed
        CheckFlippedVehicles();
        
        // Update race positions
        UpdateRacePositions();
    }
    
    private void CheckFlippedVehicles()
    {
        // Only check periodically
        if (Time.time - lastFlipCheckTime < 1.0f) return;
        
        lastFlipCheckTime = Time.time;
        
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (!player.IsLocal) continue;
            
            // Check if car is flipped (y-axis pointing down)
            if (Vector3.Dot(player.transform.up, Vector3.up) < -0.5f)
            {
                // Car is flipped
                if (playerStates.ContainsKey(player.PlayerId))
                {
                    var state = playerStates[player.PlayerId];
                    
                    if (state.flipStartTime == 0f)
                    {
                        // Start counting flip time
                        state.flipStartTime = Time.time;
                    }
                    else if (Time.time - state.flipStartTime > maxVehicleFlipTime)
                    {
                        // Flipped for too long, respawn
                        RespawnPlayer(player.PlayerId);
                        state.flipStartTime = 0f;
                    }
                }
            }
            else
            {
                // Car is upright, reset flip timer
                if (playerStates.ContainsKey(player.PlayerId))
                {
                    playerStates[player.PlayerId].flipStartTime = 0f;
                }
            }
        }
    }
    
    private void UpdateRacePositions()
    {
        // Create a sorted list of players by progress
        var sortedPlayers = playerStates.Values.ToList();
        
        // Sort by: 1) lap, 2) checkpoint, 3) time
        sortedPlayers.Sort((a, b) => {
            // First by lap (descending)
            int lapComparison = b.currentLap.CompareTo(a.currentLap);
            if (lapComparison != 0) return lapComparison;
            
            // Then by checkpoint (descending)
            int checkpointComparison = b.nextCheckpoint.CompareTo(a.nextCheckpoint);
            if (checkpointComparison != 0) return checkpointComparison;
            
            // Finally by time (ascending) - who reached the checkpoint first
            return a.lastCheckpointTime.CompareTo(b.lastCheckpointTime);
        });
        
        // Assign positions
        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            var player = sortedPlayers[i];
            player.position = i + 1;
        }
    }
    
    public void PassCheckpoint(string playerId, int checkpointIndex)
    {
        if (!playerStates.ContainsKey(playerId))
        {
            Debug.LogWarning($"Unknown player {playerId} passed checkpoint {checkpointIndex}");
            return;
        }
        
        PlayerRaceState state = playerStates[playerId];
        
        // Only process if this is the expected next checkpoint
        if (checkpointIndex != state.nextCheckpoint)
        {
            return;
        }
        
        // Update last valid position for respawning
        if (GameManager.Instance != null)
        {
            var playerController = GameManager.Instance.GetPlayerController(playerId);
            if (playerController != null)
            {
                state.lastValidPosition = playerController.transform.position;
                state.lastValidRotation = playerController.transform.rotation;
            }
        }
        
        // Update checkpoint progress
        state.lastCheckpointTime = Time.time;
        
        // Determine next checkpoint
        state.nextCheckpoint = (state.nextCheckpoint + 1) % checkpoints.Length;
        
        // If completed a lap
        if (state.nextCheckpoint == 0)
        {
            CompleteLap(playerId);
        }
        
        // Send checkpoint data to other players
        SendCheckpointData(playerId, checkpointIndex);
    }
    
    private void CompleteLap(string playerId)
    {
        PlayerRaceState state = playerStates[playerId];
        
        // Calculate lap time
        float lapTime = Time.time - state.lastLapTime;
        state.lastLapTime = Time.time;
        
        // Store lap time
        int completedLap = state.currentLap + 1;
        state.lapTimes[completedLap] = lapTime;
        
        // Update best lap time
        if (lapTime < state.bestLapTime)
        {
            state.bestLapTime = lapTime;
        }
        
        // Increment lap counter
        state.currentLap++;
        
        // Check if race is finished
        if (state.currentLap >= totalLaps)
        {
            FinishRace(playerId);
        }
        
        // Send lap completion to other players
        SendLapCompletionData(playerId, state.currentLap, lapTime);
    }
    
    private void FinishRace(string playerId)
    {
        if (!playerStates.ContainsKey(playerId))
            return;
            
        PlayerRaceState state = playerStates[playerId];
        state.finished = true;
        
        // If this is the local player, show race completed UI
        var playerController = GameManager.Instance?.GetPlayerController(playerId);
        if (playerController != null && playerController.IsLocal)
        {
            DisplayRaceResults();
        }
        
        // Send race completion to other players
        SendRaceCompletionData(playerId, state.totalRaceTime);
    }
    
    private void DisplayRaceResults()
    {
        if (raceCompletedUI != null)
        {
            raceCompletedUI.SetActive(true);
        }
        
        if (raceResultsText != null)
        {
            string results = "Race Results:\n\n";
            
            // Get sorted list of players by position
            var sortedPlayers = playerStates.Values
                .OrderBy(p => p.finished ? 0 : 1)  // Finished players first
                .ThenBy(p => p.position)           // Then by position
                .ToList();
                
            foreach (var player in sortedPlayers)
            {
                string timeStr = player.finished ? 
                    $"{player.totalRaceTime:F2}s" : "DNF";
                    
                results += $"{player.position}. {player.playerId} - {timeStr}\n";
                
                // Add best lap time if available
                if (player.bestLapTime < float.MaxValue)
                {
                    results += $"   Best Lap: {player.bestLapTime:F2}s\n";
                }
            }
            
            raceResultsText.text = results;
        }
    }
    
    public void RespawnPlayer(string playerId)
    {
        if (!playerStates.ContainsKey(playerId))
            return;
            
        var state = playerStates[playerId];
        var controller = GameManager.Instance?.GetPlayerController(playerId);
        
        if (controller == null)
            return;
        
        // Find the best respawn position
        Transform respawnPoint;
        
        if (state.nextCheckpoint > 0)
        {
            // Respawn at the last checkpoint
            respawnPoint = checkpoints[state.nextCheckpoint - 1];
        }
        else if (state.currentLap > 0)
        {
            // Respawn at the last checkpoint of the previous lap
            respawnPoint = checkpoints[checkpoints.Length - 1];
        }
        else
        {
            // Respawn at starting position
            respawnPoint = checkpoints[0];
        }
        
        // Temporarily disable controls during respawn
        controller.DisableControls();
        
        // Position slightly above the respawn point to avoid collision issues
        Vector3 respawnPosition = respawnPoint.position + Vector3.up * 1.0f;
        Quaternion respawnRotation = respawnPoint.rotation;
        
        // Apply respawn
        controller.Respawn(respawnPosition, respawnRotation);
        
        // Enable controls after a short delay
        StartCoroutine(EnableControlsAfterDelay(controller, respawnDelay));
        
        // Send respawn event to other players
        SendRespawnData(playerId, respawnPosition, respawnRotation);
    }
    
    private IEnumerator EnableControlsAfterDelay(PlayerController player, float delay)
    {
        yield return new WaitForSeconds(delay);
        player.EnableControls();
    }
    
    #region Network Events
    
    private void HandleRaceData(string fromClient, string jsonData)
    {
        try
        {
            // Parse the data format: "EVENT_TYPE|{json data}"
            string[] parts = jsonData.Split('|', 2);
            if (parts.Length < 2) return;
            
            string eventType = parts[0];
            string data = parts[1];
            
            switch (eventType)
            {
                case "CHECKPOINT":
                    HandleCheckpointData(fromClient, data);
                    break;
                case "LAP_COMPLETE":
                    HandleLapCompletionData(fromClient, data);
                    break;
                case "RACE_COMPLETE":
                    HandleRaceCompletionData(fromClient, data);
                    break;
                case "RESPAWN":
                    HandleRespawnData(fromClient, data);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing race data: {ex.Message}");
        }
    }
    
    private void HandleCheckpointData(string fromClient, string jsonData)
    {
        var data = JsonConvert.DeserializeObject<CheckpointData>(jsonData);
        
        if (!playerStates.ContainsKey(fromClient))
            return;
            
        // Update the remote player's checkpoint progress
        var state = playerStates[fromClient];
        state.nextCheckpoint = data.nextCheckpoint;
        state.lastCheckpointTime = Time.time; // Use local time for sorting
    }
    
    private void HandleLapCompletionData(string fromClient, string jsonData)
    {
        var data = JsonConvert.DeserializeObject<LapCompletionData>(jsonData);
        
        if (!playerStates.ContainsKey(fromClient))
            return;
            
        // Update the remote player's lap progress
        var state = playerStates[fromClient];
        state.currentLap = data.currentLap;
        state.lapTimes[data.currentLap] = data.lapTime;
        
        if (data.lapTime < state.bestLapTime)
        {
            state.bestLapTime = data.lapTime;
        }
    }
    
    private void HandleRaceCompletionData(string fromClient, string jsonData)
    {
        var data = JsonConvert.DeserializeObject<RaceCompletionData>(jsonData);
        
        if (!playerStates.ContainsKey(fromClient))
            return;
            
        // Update the remote player's race completion
        var state = playerStates[fromClient];
        state.finished = true;
        state.totalRaceTime = data.totalTime;
    }
    
    private void HandleRespawnData(string fromClient, string jsonData)
    {
        var data = JsonConvert.DeserializeObject<RespawnData>(jsonData);
        
        // Find the player controller
        var controller = GameManager.Instance?.GetPlayerController(fromClient);
        if (controller == null || controller.IsLocal)
            return;
            
        // Apply respawn to remote player
        controller.Respawn(data.position, data.rotation);
    }
    
    private void SendCheckpointData(string playerId, int checkpointIndex)
    {
        if (!playerStates.ContainsKey(playerId))
            return;
            
        var state = playerStates[playerId];
        
        var data = new CheckpointData
        {
            checkpoint = checkpointIndex,
            nextCheckpoint = state.nextCheckpoint,
            timestamp = Time.time
        };
        
        string jsonData = JsonConvert.SerializeObject(data);
        NetworkManager.Instance?.SendGameDataToRoom($"CHECKPOINT|{jsonData}");
    }
    
    private void SendLapCompletionData(string playerId, int lap, float lapTime)
    {
        var data = new LapCompletionData
        {
            currentLap = lap,
            lapTime = lapTime,
            timestamp = Time.time
        };
        
        string jsonData = JsonConvert.SerializeObject(data);
        NetworkManager.Instance?.SendGameDataToRoom($"LAP_COMPLETE|{jsonData}");
    }
    
    private void SendRaceCompletionData(string playerId, float totalTime)
    {
        var data = new RaceCompletionData
        {
            totalTime = totalTime,
            timestamp = Time.time
        };
        
        string jsonData = JsonConvert.SerializeObject(data);
        NetworkManager.Instance?.SendGameDataToRoom($"RACE_COMPLETE|{jsonData}");
    }
    
    private void SendRespawnData(string playerId, Vector3 position, Quaternion rotation)
    {
        var data = new RespawnData
        {
            position = position,
            rotation = rotation,
            timestamp = Time.time
        };
        
        string jsonData = JsonConvert.SerializeObject(data);
        NetworkManager.Instance?.SendGameDataToRoom($"RESPAWN|{jsonData}");
    }
    
    #endregion
    
    #region Data Classes
    
    [Serializable]
    private class CheckpointData
    {
        public int checkpoint;
        public int nextCheckpoint;
        public float timestamp;
    }
    
    [Serializable]
    private class LapCompletionData
    {
        public int currentLap;
        public float lapTime;
        public float timestamp;
    }
    
    [Serializable]
    private class RaceCompletionData
    {
        public float totalTime;
        public float timestamp;
    }
    
    [Serializable]
    private class RespawnData
    {
        public Vector3 position;
        public Quaternion rotation;
        public float timestamp;
    }
    
    #endregion
}
