using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace UltimateCarRacing.Networking {

public enum NetworkConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

[DefaultExecutionOrder(-100)] // Ensures early execution
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    public string relayServerIP = "127.0.0.1";
    public int relayTcpPort = 7777;
    public int relayUdpPort = 7778;
    public float heartbeatInterval = 5f;
    public float connectionTimeout = 10f;

    [Header("Debug")]
    public bool showDebugLogs = true;
    
    // TCP connection
    private TcpClient tcpClient;
    private NetworkStream tcpStream;
    private byte[] tcpReceiveBuffer = new byte[8192];
    private Thread tcpReceiveThread;
    private bool isRunning = false;
    private StringBuilder messageBuilder = new StringBuilder();

    // UDP connection
    private UdpClient udpClient;
    private Thread udpReceiveThread;
    private IPEndPoint relayEndpoint;

    // Room/client tracking
    private string clientId;
    private string currentRoomId;
    private bool isHost = false;
    public List<RemotePlayer> connectedPlayers = new List<RemotePlayer>();
    
    // Message handling
    private readonly object queueLock = new object();
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private float lastHeartbeatTime;
    
    // Add these fields to NetworkManager
    private Queue<float> latencyMeasurements = new Queue<float>(20); // Keep last 20 measurements
    private float _averageLatency = 0.05f; // Default to 50ms
    private float lastPingSentTime;
    private const float PING_INTERVAL = 2.0f; // Measure latency every 2 seconds

    // Events
    public delegate void MessageReceivedHandler(string fromClient, string message);
    public delegate void GameDataReceivedHandler(string fromClient, string jsonData);
    public delegate void ServerListReceivedHandler(List<GameRoom> rooms);
    public delegate void PlayerJoinedHandler(string clientId);
    public delegate void ConnectionStatusChangedHandler(NetworkConnectionState status, string message);
    public delegate void PositionResetHandler(Vector3 newPosition);
    
    public event MessageReceivedHandler OnMessageReceived;
    public event GameDataReceivedHandler OnGameDataReceived;
    public event ServerListReceivedHandler OnServerListReceived;
    public event PlayerJoinedHandler OnPlayerJoined;
    public event ConnectionStatusChangedHandler OnConnectionStatusChanged;
    public event PositionResetHandler OnPositionReset;

    private NetworkConnectionState _connectionStatus = NetworkConnectionState.Disconnected;
    public NetworkConnectionState ConnectionStatus 
    { 
        get { return _connectionStatus; }
        private set
        {
            if (_connectionStatus != value)
            {
                Debug.Log($"[Network] Connection status changing from {_connectionStatus} to {value}");
                _connectionStatus = value;
                OnConnectionStatusChanged?.Invoke(_connectionStatus, string.Empty);
            }
        }
    }

    public string ClientId 
    { 
        get { return clientId; } 
    }

    public bool IsHost
    {
        get { return isHost; }
    }

    // Add this property to expose the connected players
    public IReadOnlyList<RemotePlayer> ConnectedPlayers
    {
        get { return connectedPlayers.AsReadOnly(); }
    }

    // Add this property
    public float GetAverageLatency()
    {
        return _averageLatency;
    }

    // Add this property to NetworkManager if it's not already there
    public float AverageLatency 
    { 
        get { return _averageLatency; } 
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeNetwork();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // Process main thread actions
        lock (queueLock)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue()?.Invoke();
            }
        }

        // Send periodic heartbeats
        if (isRunning && Time.time - lastHeartbeatTime > heartbeatInterval)
        {
            SendHeartbeat();
            lastHeartbeatTime = Time.time;
        }

        // Send periodic pings to measure latency
        if (isRunning && ConnectionStatus == NetworkConnectionState.Connected && 
            Time.time - lastPingSentTime > PING_INTERVAL)
        {
            SendPing();
            lastPingSentTime = Time.time;
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }

    private void InitializeNetwork()
    {
        try
        {
            // Only initialize if not already running
            if (isRunning)
            {
                Log("Network already initialized");
                return;
            }
            
            ConnectionStatus = NetworkConnectionState.Connecting;
            
            // Initialize TCP connection
            tcpClient = new TcpClient();
            relayEndpoint = new IPEndPoint(IPAddress.Parse(relayServerIP), relayUdpPort);
            
            // Connect in a separate task to avoid blocking
            Task.Run(() => 
            {
                try
                {
                    // Set a reasonable timeout for connection
                    tcpClient.SendTimeout = 5000;
                    tcpClient.ReceiveTimeout = 5000;
                    
                    // Try to connect
                    tcpClient.Connect(relayServerIP, relayTcpPort);
                    
                    EnqueueAction(() => {
                        // We're connected, now set up the receive thread
                        SetupTcpReceive();
                        SetupUdpClient();
                        ConnectionStatus = NetworkConnectionState.Connected;
                        Log("Connected to relay server");
                    });
                }
                catch (Exception e)
                {
                    EnqueueAction(() => {
                        LogError($"Failed to connect to relay server: {e.Message}");
                        ConnectionStatus = NetworkConnectionState.Failed;
                        
                        // Make sure we clean up the TCP client if connection failed
                        if (tcpClient != null)
                        {
                            tcpClient.Close();
                            tcpClient = null;
                        }
                    });
                }
            });
            
            isRunning = true;
            lastHeartbeatTime = Time.time;
        }
        catch (Exception e)
        {
            LogError($"Network initialization failed: {e.Message}");
            ConnectionStatus = NetworkConnectionState.Failed;
        }
    }

    private void SetupTcpReceive()
    {
        try
        {
            tcpStream = tcpClient.GetStream();
            tcpReceiveThread = new Thread(TcpReceiveThread);
            tcpReceiveThread.IsBackground = true;
            tcpReceiveThread.Start();
        }
        catch (Exception e)
        {
            LogError($"TCP receive setup failed: {e.Message}");
        }
    }

    private void SetupUdpClient()
    {
        try
        {
            // Create UDP client on a random port
            udpClient = new UdpClient(0);
            udpReceiveThread = new Thread(UdpReceiveThread);
            udpReceiveThread.IsBackground = true;
            udpReceiveThread.Start();
        }
        catch (Exception e)
        {
            LogError($"UDP setup failed: {e.Message}");
        }
    }

    private void TcpReceiveThread()
    {
        try
        {
            while (isRunning && tcpClient != null && tcpClient.Connected)
            {
                byte[] headerBuffer = new byte[4];
                byte[] messageBuffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = tcpStream.Read(messageBuffer, 0, messageBuffer.Length)) > 0)
                {
                    string data = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
                    
                    // Process all messages in the buffer
                    int start = 0;
                    int end;
                    
                    // Handle messages that might be split across receives
                    messageBuilder.Append(data);
                    string combined = messageBuilder.ToString();
                    
                    // Look for newline-terminated messages
                    while ((end = combined.IndexOf('\n', start)) >= 0)
                    {
                        string message = combined.Substring(start, end - start);
                        ProcessTcpMessage(message);
                        start = end + 1;
                    }
                    
                    // Keep any remaining partial message
                    if (start < combined.Length)
                    {
                        messageBuilder.Clear();
                        messageBuilder.Append(combined.Substring(start));
                    }
                    else
                    {
                        messageBuilder.Clear();
                    }
                }
            }
        }
        catch (ThreadAbortException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            if (isRunning)
            {
                EnqueueAction(() => {
                    LogError($"TCP receive thread error: {e.Message}");
                    ConnectionStatus = NetworkConnectionState.Failed;
                    Reconnect();  // This could be creating your loop
                });
            }
        }
    }

    private void UdpReceiveThread()
    {
        try
        {
            while (isRunning && udpClient != null)
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                
                // Process received UDP data
                string message = Encoding.UTF8.GetString(data);
                ProcessUdpMessage(message, remoteEndPoint);
            }
        }
        catch (ThreadAbortException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            if (isRunning)
            {
                EnqueueAction(() => {
                    LogError($"UDP receive thread error: {e.Message}");
                });
            }
        }
    }

    private void ProcessTcpMessage(string jsonMessage)
    {
        try
        {
            Dictionary<string, object> message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            
            if (message == null || !message.ContainsKey("type"))
                return;
                
            string messageType = message["type"].ToString();
            
            EnqueueAction(() => {
                switch (messageType)
                {
                    case "REGISTERED":
                        clientId = message["client_id"].ToString();
                        Log($"Registered with relay server. Client ID: {clientId}");
                        break;
                        
                    case "HEARTBEAT_ACK":
                        // Just update last heartbeat time
                        break;
                        
                    case "GAME_HOSTED":
                        currentRoomId = message["room_id"].ToString();
                        isHost = true;
                        Log($"Game hosted successfully. Room ID: {currentRoomId}");
                        break;
                        
                    case "GAME_LIST":
                        ProcessGameList(message);
                        break;
                        
                    case "JOINED_GAME":
                        currentRoomId = message["room_id"].ToString();
                        string hostId = message["host_id"].ToString();
                        isHost = false;
                        Log($"Joined game. Room ID: {currentRoomId}, Host: {hostId}");
                        break;
                        
                    case "PLAYER_JOINED":
                        string joinedClientId = message["client_id"].ToString();
                        Log($"Player joined: {joinedClientId}");
                        
                        if (!connectedPlayers.Exists(p => p.clientId == joinedClientId))
                        {
                            connectedPlayers.Add(new RemotePlayer { clientId = joinedClientId });
                        }
                        
                        OnPlayerJoined?.Invoke(joinedClientId);
                        break;
                        
                    case "JOIN_FAILED":
                        string reason = message["reason"].ToString();
                        LogError($"Failed to join game: {reason}");
                        break;
                        
                    case "RELAY":
                        string fromClientId = message["from"].ToString();
                        string relayMessage = message["message"].ToString();
                        OnMessageReceived?.Invoke(fromClientId, relayMessage);
                        break;

                    case "PING_RESPONSE":
                        float pingTime = Convert.ToSingle(message["timestamp"]);
                        ProcessPingResponse(pingTime);
                        break;

                    case "RESET_POSITION":
                        // Extract position data
                        var posData = message["position"] as Newtonsoft.Json.Linq.JObject;
                        if (posData != null)
                        {
                            Vector3 newPosition = new Vector3(
                                Convert.ToSingle(posData["x"]),
                                Convert.ToSingle(posData["y"]),
                                Convert.ToSingle(posData["z"])
                            );
                            
                            // Trigger an event that GameManager can listen to
                            OnPositionReset?.Invoke(newPosition);
                            Log($"Received position reset command to {newPosition}");
                        }
                        break;
                }
            });
        }
        catch (Exception e)
        {
            EnqueueAction(() => {
                LogError($"Error processing TCP message: {e.Message}. Message: {jsonMessage}");
            });
        }
    }

    private void ProcessUdpMessage(string jsonMessage, IPEndPoint sender)
    {
        try
        {
            Dictionary<string, object> message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            
            if (message == null || !message.ContainsKey("type"))
                return;
                
            string messageType = message["type"].ToString();
            
            if (messageType == "GAME_DATA")
            {
                string fromClientId = message["from"].ToString();
                string gameData = message["data"].ToString();
                
                EnqueueAction(() => {
                    OnGameDataReceived?.Invoke(fromClientId, gameData);
                });
            }
        }
        catch (Exception e)
        {
            EnqueueAction(() => {
                LogError($"Error processing UDP message: {e.Message}");
            });
        }
    }

    private void ProcessGameList(Dictionary<string, object> message)
    {
        try
        {
            var roomsData = message["rooms"] as Newtonsoft.Json.Linq.JArray;
            if (roomsData == null) return;
            
            List<GameRoom> rooms = new List<GameRoom>();
            
            foreach (var roomData in roomsData)
            {
                var room = new GameRoom
                {
                    roomId = roomData["room_id"].ToString(),
                    name = roomData["name"].ToString(),
                    hostId = roomData["host_id"].ToString(),
                    playerCount = Convert.ToInt32(roomData["player_count"]),
                    maxPlayers = Convert.ToInt32(roomData["max_players"])
                };
                
                rooms.Add(room);
            }
            
            OnServerListReceived?.Invoke(rooms);
            Log($"Received list of {rooms.Count} game rooms");
        }
        catch (Exception e)
        {
            LogError($"Error processing game list: {e.Message}");
        }
    }

    private void SendHeartbeat()
    {
        if (!isRunning || tcpClient == null || !tcpClient.Connected)
        {
            // Don't try to send if we're not properly connected
            return;
        }
            
        try
        {
            SendTcpMessage(new Dictionary<string, object>
            {
                { "type", "HEARTBEAT" }
            });
            Debug.Log("Heartbeat sent");  // Add this to track heartbeat sending
        }
        catch (Exception e)
        {
            LogError($"Failed to send heartbeat: {e.Message}");
            // Don't immediately reconnect here - give the connection recovery a chance
        }
    }

    public void RequestGameList()
    {
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "LIST_GAMES" }
        });
        Log("Requested game list");
    }

    public void HostGame(string roomName, int maxPlayers = 4)
    {
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "HOST_GAME" },
            { "room_name", roomName },
            { "max_players", maxPlayers }
        });
        Log($"Requested to host game: {roomName}");
    }

    public void JoinGame(string roomId)
    {
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "JOIN_GAME" },
            { "room_id", roomId }
        });
        Log($"Requested to join game: {roomId}");
    }

    public void SendMessageToPlayer(string targetClientId, string message)
    {
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "RELAY_MESSAGE" },
            { "target_id", targetClientId },
            { "message", message }
        });
    }

    public void SendMessageToRoom(string message)
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            LogError("Cannot send message - not in a room");
            return;
        }
            
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "RELAY_MESSAGE" },
            { "room_id", currentRoomId },
            { "message", message }
        });
    }

    public void SendGameDataToPlayer(string targetClientId, string jsonData)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            LogError("Cannot send game data - not registered");
            return;
        }
            
        SendUdpMessage(new Dictionary<string, object>
        {
            { "type", "GAME_DATA" },
            { "client_id", clientId },
            { "target_id", targetClientId },
            { "data", jsonData }
        });
    }

    public void SendGameDataToRoom(string jsonData)
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            LogError("Cannot send game data - not in a room");
            return;
        }
            
        SendUdpMessage(new Dictionary<string, object>
        {
            { "type", "GAME_DATA" },
            { "client_id", clientId },
            { "room_id", currentRoomId },
            { "data", jsonData }
        });
    }

    private void SendTcpMessage(Dictionary<string, object> message)
    {
        // First check if we're running and have valid connections
        if (!isRunning)
        {
            LogError("Cannot send TCP message - network is not running");
            return;
        }
        
        if (tcpClient == null)
        {
            LogError("Cannot send TCP message - TCP client not initialized");
            return;
        }
        
        if (!tcpClient.Connected)
        {
            LogError("Cannot send TCP message - TCP client not connected");
            ConnectionStatus = NetworkConnectionState.Failed;
            Reconnect();
            return;
        }
        
        if (tcpStream == null)
        {
            LogError("Cannot send TCP message - TCP stream not initialized");
            return;
        }
        
        // Now try to send the message
        try
        {
            // Use settings to handle circular references
            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    // Ignore Unity-specific properties that cause circular references
                    IgnoreSerializableInterface = true
                }
            };
            
            string jsonMessage = JsonConvert.SerializeObject(message, settings);
            byte[] data = Encoding.UTF8.GetBytes(jsonMessage + "\n");
            tcpStream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            LogError($"Error sending TCP message: {e.Message}");
            ConnectionStatus = NetworkConnectionState.Failed;
            Reconnect();
        }
    }

    private void SendUdpMessage(Dictionary<string, object> message)
    {
        if (!isRunning || udpClient == null)
        {
            LogError("Cannot send UDP message - client not initialized");
            return;
        }
            
        try
        {
            // Use settings to handle circular references
            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    // Ignore Unity-specific properties that cause circular references
                    IgnoreSerializableInterface = true
                }
            };
            
            string jsonMessage = JsonConvert.SerializeObject(message, settings);
            byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
            udpClient.Send(data, data.Length, relayEndpoint);
        }
        catch (Exception e)
        {
            LogError($"Error sending UDP message: {e.Message}");
        }
    }

    private void SendPing()
    {
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "PING" },
            { "timestamp", Time.time }
        });
    }

    private void ProcessPingResponse(float pingTime)
    {
        float latency = Time.time - pingTime;
        
        // Add to our measurements
        latencyMeasurements.Enqueue(latency);
        if (latencyMeasurements.Count > 20)
            latencyMeasurements.Dequeue();
        
        // Recalculate average
        float sum = 0;
        foreach (float value in latencyMeasurements)
            sum += value;
        
        _averageLatency = sum / latencyMeasurements.Count;
        
        // Log latency value
        if (showDebugLogs)
            Debug.Log($"[Network] Measured latency: {latency*1000:F1}ms, Average: {_averageLatency*1000:F1}ms");
    }

    private bool isReconnecting = false;

    private void Reconnect()
    {
        // Only attempt reconnect if we're marked as failed and not already reconnecting
        if (ConnectionStatus != NetworkConnectionState.Failed || isReconnecting)
            return;
            
        Log("Attempting to reconnect...");
        isReconnecting = true;
        Disconnect(false);
        StartCoroutine(ReconnectCoroutineWithBackoff());
    }

    private IEnumerator ReconnectCoroutineWithBackoff()
    {
        // Use exponential backoff for reconnection attempts
        float backoffTime = 2f;
        int attempts = 0;
        
        while (attempts < 5 && ConnectionStatus != NetworkConnectionState.Connected)
        {
            yield return new WaitForSeconds(backoffTime);
            
            Debug.Log($"Reconnection attempt {attempts+1}/5");
            InitializeNetwork();
            
            // Wait to see if connection succeeds
            float startTime = Time.time;
            while (Time.time - startTime < 5f && 
                   ConnectionStatus == NetworkConnectionState.Connecting)
            {
                yield return new WaitForSeconds(0.5f);
            }
            
            if (ConnectionStatus == NetworkConnectionState.Connected)
            {
                Debug.Log("Reconnection successful!");
                break;
            }
            
            // Increase backoff time for next attempt
            backoffTime = Mathf.Min(backoffTime * 1.5f, 10f);
            attempts++;
        }
        
        isReconnecting = false;
        
        if (ConnectionStatus != NetworkConnectionState.Connected)
        {
            Debug.LogError("Failed to reconnect after multiple attempts");
        }
    }

    public void Disconnect(bool setStatus = true)
    {
        isRunning = false;
        
        // Clean up TCP
        if (tcpReceiveThread != null)
        {
            try {
                tcpReceiveThread.Abort();
            } catch (Exception e) {
                Debug.LogWarning($"Error aborting TCP thread: {e.Message}");
            }
            tcpReceiveThread = null;
        }
        
        if (tcpStream != null)
        {
            try {
                tcpStream.Close();
            } catch (Exception e) {
                Debug.LogWarning($"Error closing TCP stream: {e.Message}");
            }
            tcpStream = null;
        }
        
        if (tcpClient != null)
        {
            try {
                tcpClient.Close();
            } catch (Exception e) {
                Debug.LogWarning($"Error closing TCP client: {e.Message}");
            }
            tcpClient = null;
        }
        
        // Similar careful cleanup for UDP resources...
        
        // Reset state
        clientId = null;
        currentRoomId = null;
        isHost = false;
        connectedPlayers.Clear();
        
        if (setStatus)
        {
            ConnectionStatus = NetworkConnectionState.Disconnected;
        }
        
        Log("Disconnected from relay server");
    }

    public void LeaveRoom()
    {
        if (string.IsNullOrEmpty(currentRoomId))
            return;
            
        // Send explicit LEAVE_ROOM message to server
        SendTcpMessage(new Dictionary<string, object>
        {
            { "type", "LEAVE_ROOM" },
            { "room_id", currentRoomId }
        });
        
        // Reset local room state
        string oldRoomId = currentRoomId;
        currentRoomId = null;
        isHost = false;
        connectedPlayers.Clear();
        
        Log($"Left room: {oldRoomId}");
    }

    private void EnqueueAction(Action action)
    {
        lock (queueLock)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[Network] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[Network] {message}");
    }

    [System.Serializable]
    public class RemotePlayer
    {
        public string clientId;
        public string playerName;
        public float lastSeen;
    }

    [System.Serializable]
    public class GameRoom
    {
        public string roomId;
        public string name;
        public string hostId;
        public int playerCount;
        public int maxPlayers;
    }

    // Add to HandleGameData method:
    private void HandleGameData(string fromClient, string jsonData)
    {
        // Log all incoming game data for debugging
        Debug.Log($"Received game data from {fromClient}: {jsonData}");
        
        // Regular processing...
    }
}

}