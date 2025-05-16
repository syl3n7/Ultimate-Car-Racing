using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;

public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance { get; private set; }
    
    [Header("Server Settings")]
    public string serverIP = "127.0.0.1";
    public int tcpPort = 7777;
    public int udpPort = 7778;
    public float heartbeatInterval = 5f;
    public float reconnectDelay = 3f;
    
    [Header("Debug")]
    public bool showDebugMessages = false;
    
    // Connection state
    private TcpClient tcpClient;
    private UdpClient udpClient;
    private NetworkStream tcpStream;
    private IPEndPoint serverEndPoint;
    private IPEndPoint udpServerEndPoint;
    private Thread tcpListenThread;
    private Thread udpListenThread;
    private bool isConnected = false;
    private bool isConnecting = false;
    private bool tcpThreadRunning = false;
    private bool udpThreadRunning = false;
    private string clientId;
    private string currentRoomId;
    private string hostId;
    private bool isHost;
    
    // Message events
    public delegate void MessageReceivedHandler(Dictionary<string, object> message);
    public event MessageReceivedHandler OnRoomPlayersReceived;
    public event MessageReceivedHandler OnRoomListReceived;
    public event MessageReceivedHandler OnGameStarted;
    public event MessageReceivedHandler OnJoinedGame;
    public event MessageReceivedHandler OnPlayerJoined;
    public event MessageReceivedHandler OnPlayerDisconnected;
    public event MessageReceivedHandler OnRelayReceived;
    public event MessageReceivedHandler OnGameHosted;
    public event MessageReceivedHandler OnKicked;
    public event MessageReceivedHandler OnServerMessage;
    
    // Connection events
    public delegate void ConnectionEventHandler();
    public event ConnectionEventHandler OnConnected;
    public event ConnectionEventHandler OnDisconnected;
    public event ConnectionEventHandler OnConnectionFailed;
    
    // Latency calculation
    private float latency = 0f;
    private float lastPingTime = 0f;
    private const float pingInterval = 2f;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("NetworkClient initialized as singleton");
            // Register for scene load events
           UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Debug.Log("Duplicate NetworkClient destroyed");
            Destroy(gameObject);
        }
    }
    
    private void OnApplicationQuit()
    {
        Disconnect();
    }
    
    private void Update()
    {
        // Send periodic ping to calculate latency
        if (isConnected && Time.time - lastPingTime > pingInterval)
        {
            SendPing();
            lastPingTime = Time.time;
        }
    }
    
    private void SendPing()
    {
        Dictionary<string, object> pingMessage = new Dictionary<string, object>
        {
            { "type", "PING" },
            { "timestamp", DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond }
        };
        
        SendTcpMessage(pingMessage);
    }
    
    public void Connect()
    {
        if (isConnected || isConnecting)
            return;
            
        isConnecting = true;
        serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), tcpPort);
        udpServerEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), udpPort);
        
        StartCoroutine(ConnectToServer());
    }
    
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        string sceneName = scene.name;
        Debug.Log($"NetworkClient: Scene loaded: {sceneName}");
        
        // If we're in a race scene and connected to server, 
        // ensure the UDP listener is active
        if (isConnected && (sceneName.Contains("Track") || sceneName.Contains("Race")))
        {
            // If we need to reconnect or re-establish UDP after scene change
            if (udpClient == null || !udpThreadRunning)
            {
                try {
                    if (udpClient == null)
                    {
                        udpClient = new UdpClient();
                        udpClient.Connect(udpServerEndPoint);
                        Debug.Log("Recreated UDP client after scene change");
                    }
                    
                    if (!udpThreadRunning && udpListenThread == null)
                    {
                        udpThreadRunning = true;
                        udpListenThread = new Thread(new ThreadStart(UdpListenForMessages));
                        udpListenThread.IsBackground = true;
                        udpListenThread.Start();
                        Debug.Log("Restarted UDP listener thread after scene change");
                    }
                }
                catch (Exception e) {
                    Debug.LogError($"Error reconnecting UDP after scene change: {e.Message}");
                }
            }
            
            // Send scene ready notification after delay to give scene time to load
            StartCoroutine(SendSceneReadyAfterDelay(2.0f));
        }
    }

    private IEnumerator SendSceneReadyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (isConnected && !string.IsNullOrEmpty(currentRoomId))
        {
            Dictionary<string, object> readyMessage = new Dictionary<string, object>
            {
                { "type", "RELAY_MESSAGE" },
                { "room_id", currentRoomId },
                { "message", new Dictionary<string, object> {
                    { "type", "SCENE_READY" },
                    { "player_id", clientId }
                }}
            };
            
            SendTcpMessage(readyMessage);
            Debug.Log("Sent scene ready notification after scene change");
        }
    }
    
    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private IEnumerator ConnectToServer()
    {
        while (isConnecting)
        {
            try
            {
                // Establish TCP connection
                tcpClient = new TcpClient();
                tcpClient.Connect(serverEndPoint);
                tcpStream = tcpClient.GetStream();

                // Create UDP client
                udpClient = new UdpClient();
                udpClient.Connect(udpServerEndPoint);

                isConnected = true;
                isConnecting = false;

                // Start listening threads
                StartListeningThreads();

                // Start heartbeat
                InvokeRepeating("SendHeartbeat", 0f, heartbeatInterval);

                // Send initial registration message
                SendRegistration();

                if (OnConnected != null)
                    OnConnected.Invoke();

                LogDebug("Connected to server successfully");
                yield break;
            }
            catch (Exception e)
            {
                LogDebug($"Connection failed: {e.Message}. Retrying in {reconnectDelay} seconds...");

                // Clean up any partial connections
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }

                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                }
            }

            yield return new WaitForSeconds(reconnectDelay);
        }

        if (OnConnectionFailed != null)
            OnConnectionFailed.Invoke();
    }
    
    private void SendRegistration()
    {
        // Send initial registration message that includes client info
        Dictionary<string, object> registrationMessage = new Dictionary<string, object>
        {
            { "type", "REGISTER" },
            { "client_info", new Dictionary<string, object> {
                { "name", SystemInfo.deviceName },
                { "platform", Application.platform.ToString() },
                { "version", Application.version }
            }}
        };
        
        SendTcpMessage(registrationMessage);
    }
    
    private void StartListeningThreads()
    {
        // Start TCP listener thread
        tcpThreadRunning = true;
        tcpListenThread = new Thread(new ThreadStart(TcpListenForMessages));
        tcpListenThread.IsBackground = true;
        tcpListenThread.Start();
        
        // Start UDP listener thread
        udpThreadRunning = true;
        udpListenThread = new Thread(new ThreadStart(UdpListenForMessages));
        udpListenThread.IsBackground = true;
        udpListenThread.Start();
    }
    
    private void TcpListenForMessages()
    {
        byte[] receiveBuffer = new byte[8192];
        StringBuilder messageBuilder = new StringBuilder();
        
        while (tcpThreadRunning && tcpClient != null && tcpClient.Connected)
        {
            try
            {
                int bytesRead = tcpStream.Read(receiveBuffer, 0, receiveBuffer.Length);
                
                if (bytesRead <= 0)
                {
                    // Connection closed
                    HandleDisconnect();
                    return;
                }
                
                string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
                messageBuilder.Append(data);
                
                // Process any complete messages - messages are now newline delimited for the new server
                string messages = messageBuilder.ToString();
                int newlineIndex;
                
                while ((newlineIndex = messages.IndexOf('\n')) != -1)
                {
                    string message = messages.Substring(0, newlineIndex);
                    messages = messages.Substring(newlineIndex + 1);
                    
                    ProcessTcpMessage(message);
                }
                
                messageBuilder.Clear();
                messageBuilder.Append(messages);
            }
            catch (ThreadAbortException)
            {
                // Thread is being aborted, exit cleanly
                break;
            }
            catch (Exception e)
            {
                if (tcpThreadRunning)
                {
                    LogDebug($"TCP listener error: {e.Message}");
                    HandleDisconnect();
                    return;
                }
            }
        }
    }
    
    private void UdpListenForMessages()
    {
        while (udpThreadRunning && udpClient != null)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(data);
                
                ProcessUdpMessage(message);
            }
            catch (ThreadAbortException)
            {
                // Thread is being aborted, exit cleanly
                break;
            }
            catch (Exception e)
            {
                if (udpThreadRunning)
                {
                    LogDebug($"UDP listener error: {e.Message}");
                }
            }
        }
    }
    
    private void ProcessTcpMessage(string jsonMessage)
    {
        try
        {
            Dictionary<string, object> message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            string messageType = message["type"].ToString();

            switch (messageType)
            {
                case "RELAY":
                    if (OnRelayReceived != null)
                    {
                        // Check if this is a SCENE_READY message
                        Dictionary<string, object> relayData = message["message"] as Dictionary<string, object>;
                        if (relayData != null && relayData.ContainsKey("type") && relayData["type"].ToString() == "SCENE_READY")
                        {
                            string readyPlayerId = relayData["player_id"].ToString();
                            Debug.Log($"Received SCENE_READY from player {readyPlayerId}");
                            
                            // Force request the player's state to ensure we have it
                            if (GameManager.Instance != null && readyPlayerId != clientId)
                            {
                                Dictionary<string, object> stateRequest = new Dictionary<string, object>
                                {
                                    { "type", "RELAY_MESSAGE" },
                                    { "target_id", readyPlayerId },
                                    { "message", new Dictionary<string, object> {
                                        { "type", "REQUEST_STATE" },
                                        { "player_id", clientId }
                                    }}
                                };
                                
                                SendTcpMessage(stateRequest);
                                Debug.Log($"Requesting initial state from player {readyPlayerId}");
                            }
                        }
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnRelayReceived.Invoke(message));
                    }
                    break;

                case "ROOM_PLAYERS":
                    if (OnRoomPlayersReceived != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnRoomPlayersReceived.Invoke(message));
                    break;

                case "REGISTERED":
                    clientId = message["client_id"].ToString();
                    LogDebug($"Registered with server, client ID: {clientId}");
                    break;

                case "HEARTBEAT_ACK":
                    // Just acknowledge, no action needed
                    break;

                case "PING_RESPONSE":
                    if (message.ContainsKey("timestamp"))
                    {
                        long sentTime = Convert.ToInt64(message["timestamp"]);
                        long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                        latency = (now - sentTime);
                    }
                    break;

                case "GAME_LIST":
                    if (OnRoomListReceived != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnRoomListReceived.Invoke(message));
                    break;

                case "GAME_HOSTED":
                    currentRoomId = message["room_id"].ToString();
                    if (OnGameHosted != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnGameHosted.Invoke(message));
                    break;

                case "GAME_STARTED":
                    if (OnGameStarted != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnGameStarted.Invoke(message));
                    break;

                case "JOINED_GAME":
                    if (message.ContainsKey("room_id") && message.ContainsKey("host_id"))
                    {
                        currentRoomId = message["room_id"].ToString();
                        hostId = message["host_id"].ToString(); // Store the host ID
                        
                        // Check if we are the host
                        isHost = (hostId == clientId);
                        
                        // Call the event handler on the main thread
                        if (OnJoinedGame != null)
                            UnityMainThreadDispatcher.Instance().Enqueue(() => OnJoinedGame.Invoke(message));
                    }
                    break;

                case "PLAYER_JOINED":
                    if (OnPlayerJoined != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnPlayerJoined.Invoke(message));
                    break;

                case "PLAYER_DISCONNECTED":
                    if (OnPlayerDisconnected != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnPlayerDisconnected.Invoke(message));
                    break;

                case "KICKED":
                    if (OnKicked != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnKicked.Invoke(message));
                    HandleDisconnect();
                    break;

                case "SERVER_MESSAGE":
                    if (OnServerMessage != null)
                        UnityMainThreadDispatcher.Instance().Enqueue(() => OnServerMessage.Invoke(message));
                    break;

                case "RESET_POSITION":
                    var position = message["position"] as Newtonsoft.Json.Linq.JObject;
                    Vector3 resetPos = new Vector3(
                        Convert.ToSingle(position["x"]),
                        Convert.ToSingle(position["y"]),
                        Convert.ToSingle(position["z"])
                    );

                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        if (GameManager.Instance != null)
                            GameManager.Instance.RespawnPlayer(clientId);
                    });
                    break;

                default:
                    LogDebug($"Unknown message type: {messageType}");
                    break;
            }
        }
        catch (Exception e)
        {
            LogDebug($"Error processing TCP message: {e.Message}");
        }
    }
    
    private void ProcessUdpMessage(string jsonMessage)
    {
        try
        {
            Dictionary<string, object> message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            
            string messageType = message["type"].ToString();
            
            // Get sender ID from either "from" or "client_id" field
            string fromField = message.ContainsKey("from") ? "from" : 
                            message.ContainsKey("client_id") ? "client_id" : 
                            message.ContainsKey("player_id") ? "player_id" : null;
            
            if (fromField == null || !message.ContainsKey(fromField))
            {
                Debug.LogWarning($"UDP message missing sender ID: {jsonMessage}");
                return;
            }
            
            string fromClientId = message[fromField].ToString();
            
            // Skip our own messages
            if (fromClientId == clientId)
                return;
            
            // Check if this is from the host when we're a client
            bool isFromHost = fromClientId == hostId && clientId != hostId;
            
            // Process GAME_DATA messages
            if (messageType == "GAME_DATA" && message.ContainsKey("data"))
            {
                var gameData = message["data"] as Dictionary<string, object>;
                if (gameData != null && gameData.ContainsKey("type"))
                {
                    string dataType = gameData["type"].ToString();
                    
                    // For PLAYER_STATE, extract state and pass to GameManager
                    if (dataType == "PLAYER_STATE" && gameData.ContainsKey("state"))
                    {
                        var stateData = gameData["state"] as Dictionary<string, object>;
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            if (GameManager.Instance != null)
                            {
                                // Check if player needs to be spawned
                                bool playerExists = GameManager.Instance.IsPlayerActive(fromClientId);
                                
                                // Create state data
                                var playerState = new GameManager.PlayerStateData
                                {
                                    playerId = fromClientId,
                                    position = ParseVector3(stateData["position"]),
                                    rotation = ParseQuaternion(stateData["rotation"]),
                                    velocity = ParseVector3(stateData["velocity"]),
                                    angularVelocity = ParseVector3(stateData["angularVelocity"]),
                                    timestamp = Convert.ToSingle(stateData["timestamp"])
                                };
                                
                                // We ALWAYS spawn the host's car if we're a client, or if the player doesn't exist yet
                                if (!playerExists)
                                {
                                    GameManager.Instance.SpawnRemotePlayer(
                                        fromClientId, 
                                        playerState.position, 
                                        playerState.rotation
                                    );
                                }
                                
                                // Apply state (teleport if new spawn)
                                GameManager.Instance.ApplyPlayerState(playerState, !playerExists);
                            }
                        });
                    }
                    else if (dataType == "PLAYER_INPUT" && gameData.ContainsKey("input"))
                    {
                        // Handle player input messages
                        var inputData = gameData["input"] as Dictionary<string, object>;
                        if (inputData != null)
                        {
                            var playerInput = new GameManager.PlayerInputData
                            {
                                playerId = fromClientId,
                                steering = Convert.ToSingle(inputData["steering"]),
                                throttle = Convert.ToSingle(inputData["throttle"]),
                                brake = Convert.ToSingle(inputData["brake"]),
                                timestamp = Convert.ToSingle(inputData["timestamp"])
                            };
                            
                            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            {
                                if (GameManager.Instance != null)
                                {
                                    GameManager.Instance.ApplyPlayerInput(playerInput);
                                }
                            });
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing UDP message: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private Vector3 ParseVector3(object vectorObject)
    {
        var vectorData = vectorObject as Dictionary<string, object>;
        if (vectorData != null)
        {
            return new Vector3(
                Convert.ToSingle(vectorData["x"]),
                Convert.ToSingle(vectorData["y"]),
                Convert.ToSingle(vectorData["z"])
            );
        }
        return Vector3.zero;
    }
    
    private Quaternion ParseQuaternion(object quaternionObject)
    {
        var quaternionData = quaternionObject as Dictionary<string, object>;
        if (quaternionData != null)
        {
            return new Quaternion(
                Convert.ToSingle(quaternionData["x"]),
                Convert.ToSingle(quaternionData["y"]),
                Convert.ToSingle(quaternionData["z"]),
                Convert.ToSingle(quaternionData["w"])
            );
        }
        return Quaternion.identity;
    }
    
    public void Disconnect()
    {
        // Send disconnect message if connected
        if (isConnected && tcpClient != null && tcpClient.Connected)
        {
            Dictionary<string, object> disconnectMessage = new Dictionary<string, object>
            {
                { "type", "DISCONNECT" }
            };
            
            try
            {
                SendTcpMessage(disconnectMessage);
            }
            catch (Exception)
            {
                // Ignore errors when trying to send disconnect message
            }
        }
        
        // Stop threads
        tcpThreadRunning = false;
        udpThreadRunning = false;
        
        if (tcpListenThread != null && tcpListenThread.IsAlive)
        {
            tcpListenThread.Abort();
            tcpListenThread = null;
        }
        
        if (udpListenThread != null && udpListenThread.IsAlive)
        {
            udpListenThread.Abort();
            udpListenThread = null;
        }
        
        // Close connections
        if (tcpClient != null)
        {
            if (tcpClient.Connected)
                tcpClient.Close();
            tcpClient = null;
        }
        
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
        
        // Cancel heartbeat
        CancelInvoke("SendHeartbeat");
        
        isConnected = false;
        isConnecting = false;
        clientId = null;
        currentRoomId = null;
        
        if (OnDisconnected != null)
            OnDisconnected.Invoke();
            
        LogDebug("Disconnected from server");
    }
    
    private void HandleDisconnect()
    {
        if (isConnected)
        {
            isConnected = false;
            
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                Disconnect();
            });
        }
    }
    
    private void SendHeartbeat()
    {
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "HEARTBEAT" }
        };
        
        SendTcpMessage(message);
    }
    
    public void SendTcpMessage(Dictionary<string, object> message)
    {
        if (!isConnected || tcpClient == null || !tcpClient.Connected)
            return;
            
        try
        {
            // Ensure we include client_id in each message for the new server
            if (!message.ContainsKey("client_id") && !string.IsNullOrEmpty(clientId))
            {
                message["client_id"] = clientId;
            }
            
            string jsonMessage = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(jsonMessage + "\n");
            tcpStream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            LogDebug($"Error sending TCP message: {e.Message}");
            HandleDisconnect();
        }
    }
    
    public void SendUdpMessage(Dictionary<string, object> message)
    {
        if (!isConnected || udpClient == null)
            return;
            
        try
        {
            // Ensure we include client_id in UDP messages
            if (!message.ContainsKey("client_id") && !string.IsNullOrEmpty(clientId))
            {
                message["client_id"] = clientId;
            }
            
            string jsonMessage = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
            udpClient.Send(data, data.Length);
        }
        catch (Exception e)
        {
            LogDebug($"Error sending UDP message: {e.Message}");
        }
    }
    
    public void HostGame(string roomName, int maxPlayers = 20)
    {
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "HOST_GAME" },
            { "room_name", roomName },
            { "max_players", maxPlayers }
        };
        
        SendTcpMessage(message);
    }
    
    public void JoinGame(string roomId)
    {
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "JOIN_GAME" },
            { "room_id", roomId }
        };
        
        SendTcpMessage(message);
    }
    
    public void StartGame()
    {
        if (string.IsNullOrEmpty(currentRoomId))
            return;
            
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "START_GAME" },
            { "room_id", currentRoomId }
        };
        
        SendTcpMessage(message);
    }
    
    public void LeaveGame()
    {
        if (string.IsNullOrEmpty(currentRoomId))
            return;
            
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "LEAVE_ROOM" },
            { "room_id", currentRoomId }
        };
        
        SendTcpMessage(message);
        currentRoomId = null;
    }
    
    public void RequestRoomList()
    {
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "LIST_GAMES" }
        };
        
        SendTcpMessage(message);
    }
    
    public void SendPlayerState(GameManager.PlayerStateData stateData)
    {
        if (isConnected && udpClient != null)
        {
            try
            {
                // Create the properly formatted message
                var message = new Dictionary<string, object>
                {
                    ["type"] = "GAME_DATA",
                    ["client_id"] = clientId,
                    ["room_id"] = currentRoomId,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["type"] = "PLAYER_STATE",
                        ["state"] = new Dictionary<string, object>
                        {
                            ["position"] = new Dictionary<string, float>
                            {
                                ["x"] = stateData.position.x,
                                ["y"] = stateData.position.y,
                                ["z"] = stateData.position.z
                            },
                            ["rotation"] = new Dictionary<string, float>
                            {
                                ["x"] = stateData.rotation.x,
                                ["y"] = stateData.rotation.y,
                                ["z"] = stateData.rotation.z,
                                ["w"] = stateData.rotation.w
                            },
                            ["velocity"] = new Dictionary<string, float>
                            {
                                ["x"] = stateData.velocity.x,
                                ["y"] = stateData.velocity.y,
                                ["z"] = stateData.velocity.z
                            },
                            ["angularVelocity"] = new Dictionary<string, float>
                            {
                                ["x"] = stateData.angularVelocity.x,
                                ["y"] = stateData.angularVelocity.y,
                                ["z"] = stateData.angularVelocity.z
                            },
                            ["timestamp"] = stateData.timestamp
                        }
                    }
                };

                string jsonData = JsonConvert.SerializeObject(message);
                byte[] data = Encoding.UTF8.GetBytes(jsonData);
                
                // Send using the correct method for a connected UdpClient
                udpClient.Send(data, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending player state: {e.Message}");
            }
        }
    }
    
    public void SendPlayerInput(GameManager.PlayerInputData input)
    {
        if (string.IsNullOrEmpty(currentRoomId) || string.IsNullOrEmpty(clientId))
            return;
            
        Dictionary<string, object> inputData = new Dictionary<string, object>
        {
            { "steering", input.steering },
            { "throttle", input.throttle },
            { "brake", input.brake },
            { "timestamp", input.timestamp }
        };
        
        Dictionary<string, object> gameData = new Dictionary<string, object>
        {
            { "type", "PLAYER_INPUT" },
            { "input", inputData }
        };
        
        Dictionary<string, object> message = new Dictionary<string, object>
        {
            { "type", "GAME_DATA" },
            { "client_id", clientId },
            { "room_id", currentRoomId },
            { "data", gameData }
        };
        
        SendUdpMessage(message);
    }
    
    public string GetClientId()
    {
        return clientId;
    }
    
    public string GetCurrentRoomId()
    {
        return currentRoomId;
    }
    
    public float GetLatency()
    {
        return latency;
    }
    
    public bool IsConnected()
    {
        return isConnected;
    }
    
    private void LogDebug(string message)
    {
        if (showDebugMessages)
            Debug.Log($"[NetworkClient] {message}");
    }
}