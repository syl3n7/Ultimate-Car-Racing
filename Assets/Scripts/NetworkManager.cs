using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }
    
    [Header("Server Settings")]
    public string serverIP = "127.0.0.1";
    public int tcpPort = 7777;
    public int udpPort = 7778;
    public float heartbeatInterval = 5f;
    
    [Header("Debug")]
    public bool showDebugMessages = false;
    
    private TcpClient _tcpClient;
    private UdpClient _udpClient;
    private NetworkStream _tcpStream;
    private CancellationTokenSource _cts;
    private StringBuilder _messageBuffer = new StringBuilder();
    private string _clientId;
    private string _currentRoomId;
    private string _hostId;
    private bool _isConnected = false;
    private bool _isHost = false;
    private float _lastHeartbeatTime = 0f;
    private float _lastPingTime = 0f;
    private float _latency = 0f;
    private const float PING_INTERVAL = 2f;
    
    // Events
    public event Action<string> OnConnected;
    public event Action<string> OnDisconnected;
    public event Action<string> OnConnectionFailed;
    public event Action<Dictionary<string, object>> OnRoomJoined;
    public event Action<Dictionary<string, object>> OnRoomListReceived;
    public event Action<Dictionary<string, object>> OnGameStarted;
    public event Action<Dictionary<string, object>> OnGameHosted;
    public event Action<Dictionary<string, object>> OnPlayerJoined;
    public event Action<Dictionary<string, object>> OnPlayerDisconnected;
    public event Action<Dictionary<string, object>> OnRoomPlayersReceived;
    public event Action<Dictionary<string, object>> OnRelayReceived;
    public event Action<Dictionary<string, object>> OnServerMessage;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("NetworkManager initialized as singleton");
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Debug.Log("Duplicate NetworkManager destroyed");
            Destroy(gameObject);
        }
    }
    
    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        Disconnect();
    }
    
    private void Update()
    {
        if (_isConnected)
        {
            // Send periodic heartbeat
            if (Time.time - _lastHeartbeatTime > heartbeatInterval)
            {
                SendHeartbeat();
                _lastHeartbeatTime = Time.time;
            }
            
            // Send periodic ping to calculate latency
            if (Time.time - _lastPingTime > PING_INTERVAL)
            {
                SendPing();
                _lastPingTime = Time.time;
            }
        }
    }
    
    private void OnApplicationQuit()
    {
        Disconnect();
    }
    
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        string sceneName = scene.name;
        Debug.Log($"NetworkManager: Scene loaded: {sceneName}");
        
        if (_isConnected && (sceneName.Contains("Track") || sceneName.Contains("Race")))
        {
            // Send scene ready notification after delay
            StartCoroutine(SendSceneReadyAfterDelay(2.0f));
        }
    }
    
    private System.Collections.IEnumerator SendSceneReadyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (_isConnected && !string.IsNullOrEmpty(_currentRoomId))
        {
            var readyMessage = new Dictionary<string, object>
            {
                { "type", "RELAY_MESSAGE" },
                { "room_id", _currentRoomId },
                { "message", new Dictionary<string, object> {
                    { "type", "SCENE_READY" },
                    { "player_id", _clientId }
                }}
            };
            
            SendTcpMessage(readyMessage);
            Debug.Log("Sent scene ready notification after scene change");
        }
    }
    
    public async Task Connect()
    {
        if (_isConnected) return;
        
        try
        {
            _cts = new CancellationTokenSource();
            
            // TCP Connection
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(serverIP, tcpPort);
            _tcpStream = _tcpClient.GetStream();
            
            // UDP Connection
            _udpClient = new UdpClient();
            _udpClient.Connect(serverIP, udpPort);
            
            _isConnected = true;
            
            // Start listening tasks
            _ = Task.Run(ListenForTcpMessages, _cts.Token);
            _ = Task.Run(ListenForUdpMessages, _cts.Token);
            
            // Reset buffer
            _messageBuffer.Clear();
            
            // Send registration message
            await SendRegistration();
            
            OnConnected?.Invoke("Connected successfully");
            LogDebug("Connected to server successfully");
        }
        catch (Exception e)
        {
            LogDebug($"Connection failed: {e.Message}");
            OnConnectionFailed?.Invoke(e.Message);
            
            // Clean up
            _tcpClient?.Close();
            _udpClient?.Close();
            _cts?.Cancel();
        }
    }
    
    private async Task SendRegistration()
    {
        // Use lowercase "command" field as shown in the SERVER-README.md examples
        var registrationMessage = new Dictionary<string, object>
        {
            { "command", "NAME" },
            { "name", SystemInfo.deviceName }
        };
        
        await SendTcpMessage(registrationMessage);
    }
    
    private async Task ListenForTcpMessages()
    {
        byte[] buffer = new byte[4096];
        
        while (!_cts.IsCancellationRequested && _tcpClient.Connected)
        {
            try
            {
                int bytesRead = await _tcpStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (bytesRead == 0) break; // Connection closed
                
                string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _messageBuffer.Append(data);
                
                // Process complete messages (newline-delimited)
                string messages = _messageBuffer.ToString();
                int newlineIndex;
                
                while ((newlineIndex = messages.IndexOf('\n')) != -1)
                {
                    string message = messages.Substring(0, newlineIndex);
                    messages = messages.Substring(newlineIndex + 1);
                    
                    ProcessServerMessage(message);
                }
                
                _messageBuffer.Clear();
                _messageBuffer.Append(messages);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                LogDebug($"TCP Error: {e.Message}");
                break;
            }
        }
        
        // If we exited the loop unexpectedly, disconnect
        if (_isConnected && !_cts.IsCancellationRequested)
        {
            await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => Disconnect());
        }
    }
    
    private async Task ListenForUdpMessages()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                ProcessUdpMessage(Encoding.UTF8.GetString(result.Buffer));
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                LogDebug($"UDP Error: {e.Message}");
                
                // Only break if we're still supposed to be connected
                if (_isConnected && !_cts.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
    
    // Update ProcessServerMessage method to add more debugging
    private void ProcessServerMessage(string message)
    {
        try
        {
            Debug.Log($"Received message from server: {message}");
            
            // Check if the message contains a pipe character, which might indicate an error or command prefix
            if (message.Contains("|"))
            {
                string[] parts = message.Split(new char[] { '|' }, 2);
                string command = parts[0];
                string jsonContent = parts.Length > 1 ? parts[1] : "{}";
                
                Debug.Log($"Detected pipe format: Command={command}, Content={jsonContent}");
                
                // Handle specific command types
                if (command == "UNKNOWN_COMMAND")
                {
                    // Try to parse the JSON content
                    try 
                    {
                        var jsonObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                        if (jsonObj != null && jsonObj.ContainsKey("TYPE"))
                        {
                            string originalCommand = jsonObj["TYPE"].ToString();
                            Debug.LogWarning($"Server reported unknown command: {originalCommand}");
                            
                            // If this was a REGISTER command, try the proper format
                            if (originalCommand == "REGISTER")
                            {
                                Debug.Log("Retrying registration with correct format");
                                _ = SendRegistration();
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        Debug.LogError($"Error parsing JSON content after pipe: {jsonEx.Message}");
                    }
                    return;
                }
                else
                {
                    // For other pipe-delimited messages, try to handle them based on your documentation
                    Debug.LogWarning($"Received pipe-delimited message with command: {command}");
                    // You could add special handling for other pipe-delimited messages here if needed
                    return;
                }
            }
            
            // Standard JSON processing for non-pipe messages
            var messageObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
            if (messageObj == null)
            {
                Debug.LogError("Failed to parse message as JSON");
                return;
            }
            
            if (!messageObj.ContainsKey("command") && !messageObj.ContainsKey("type"))
            {
                Debug.LogError("Message missing both 'command' and 'type' properties");
                return;
            }
            
            // Check for command field first (from SERVER-README.md)
            string messageType = messageObj.ContainsKey("command") ? 
                messageObj["command"].ToString() : 
                messageObj["type"].ToString();
                
            Debug.Log($"Processing JSON message of type: {messageType}");
            
            // Handle messages based on command/type
            switch (messageType)
            {
                case "CONNECTED":
                    if (messageObj.ContainsKey("sessionId"))
                    {
                        _clientId = messageObj["sessionId"].ToString();
                        _isConnected = true;
                        LogDebug($"Connected with session ID: {_clientId}");
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnConnected?.Invoke("Connected successfully"));
                    }
                    break;
                    
                case "REGISTERED":
                    if (messageObj.ContainsKey("client_id"))
                    {
                        _clientId = messageObj["client_id"].ToString();
                        LogDebug($"Registered with server, client ID: {_clientId}");
                    }
                    break;
                    
                case "NAME_OK":
                    LogDebug($"Name acknowledged by server");
                    break;
                    
                case "ROOM_CREATED":
                    if (messageObj.ContainsKey("roomId") && messageObj.ContainsKey("name"))
                    {
                        _currentRoomId = messageObj["roomId"].ToString();
                        _isHost = true;
                        
                        var roomCreatedMsg = new Dictionary<string, object>
                        {
                            { "room_id", _currentRoomId },
                            { "room_name", messageObj["name"].ToString() }
                        };
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnGameHosted?.Invoke(roomCreatedMsg));
                        
                        LogDebug($"Room created: {_currentRoomId}, {messageObj["name"]}");
                    }
                    break;
                    
                case "JOIN_OK":
                    if (messageObj.ContainsKey("roomId"))
                    {
                        _currentRoomId = messageObj["roomId"].ToString();
                        
                        var joinedMsg = new Dictionary<string, object>
                        {
                            { "room_id", _currentRoomId },
                            { "host_id", _hostId ?? _clientId }
                        };
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnRoomJoined?.Invoke(joinedMsg));
                        
                        LogDebug($"Joined room: {_currentRoomId}");
                    }
                    break;
                    
                case "PONG":
                    long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                    _latency = (now - _lastPingTime);
                    break;
                
                // Add other JSON message types from your documentation as needed
                    
                default:
                    LogDebug($"Unhandled message type: {messageType}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing server message: {e.Message}\nMessage: {message}");
        }
    }
    
    private void ProcessUdpMessage(string jsonMessage)
    {
        try
        {
            var message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            
            // Skip if message is invalid or from self
            if (message == null || !message.ContainsKey("type")) return;
            
            // Get sender ID from various possible fields
            string fromField = message.ContainsKey("from") ? "from" : 
                            message.ContainsKey("client_id") ? "client_id" : 
                            message.ContainsKey("player_id") ? "player_id" : null;
            
            if (fromField == null || !message.ContainsKey(fromField)) return;
            
            string fromClientId = message[fromField].ToString();
            
            // Skip our own messages
            if (fromClientId == _clientId) return;
            
            string messageType = message["type"].ToString();
            
            // Process GAME_DATA messages
            if (messageType == "GAME_DATA" && message.ContainsKey("data"))
            {
                var gameData = message["data"] as Dictionary<string, object>;
                if (gameData != null && gameData.containsKey("type"))
                {
                    string dataType = gameData["type"].ToString();
                    
                    // Handle PLAYER_STATE
                    if (dataType == "PLAYER_STATE" && gameData.ContainsKey("state"))
                    {
                        var stateData = gameData["state"] as Dictionary<string, object>;
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            if (GameManager.Instance != null)
                            {
                                bool playerExists = GameManager.Instance.IsPlayerActive(fromClientId);
                                
                                var playerState = new GameManager.PlayerStateData
                                {
                                    playerId = fromClientId,
                                    position = ParseVector3(stateData["position"]),
                                    rotation = ParseQuaternion(stateData["rotation"]),
                                    velocity = ParseVector3(stateData["velocity"]),
                                    angularVelocity = ParseVector3(stateData["angularVelocity"]),
                                    timestamp = Convert.ToSingle(stateData["timestamp"])
                                };
                                
                                if (!playerExists)
                                {
                                    GameManager.Instance.SpawnRemotePlayer(
                                        fromClientId, 
                                        playerState.position, 
                                        playerState.rotation
                                    );
                                }
                                
                                GameManager.Instance.ApplyPlayerState(playerState, !playerExists);
                            }
                        });
                    }
                    // Handle PLAYER_INPUT
                    else if (dataType == "PLAYER_INPUT" && gameData.ContainsKey("input"))
                    {
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
            LogDebug($"UDP message processing error: {e.Message}");
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
    
    // Modify SendTcpMessage to add better logging
    public async Task SendTcpMessage(object message)
    {
        if (!_isConnected || _tcpClient == null || !_tcpClient.Connected) 
        {
            Debug.LogError("Cannot send TCP message: Not connected to server");
            return;
        }
            
        try
        {
            // Ensure client_id is included if we have one
            if (message is Dictionary<string, object> dict && !dict.ContainsKey("client_id") && !string.IsNullOrEmpty(_clientId))
            {
                dict["client_id"] = _clientId;
            }
            
            string json = JsonConvert.SerializeObject(message);
            Debug.Log($"Sending TCP message: {json}");
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await _tcpStream.WriteAsync(data, 0, data.Length, _cts.Token);
            Debug.Log("TCP message sent successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending TCP message: {e.Message}");
            
            // If serious error, disconnect
            if (!_tcpClient.Connected)
            {
                await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => Disconnect());
            }
        }
    }
    
    public void SendUdpMessage(object message)
    {
        if (!_isConnected || _udpClient == null) return;
        
        try
        {
            // Ensure client_id is included if we have one
            if (message is Dictionary<string, object> dict && !dict.ContainsKey("client_id") && !string.IsNullOrEmpty(_clientId))
            {
                dict["client_id"] = _clientId;
            }
            
            string json = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            _udpClient.Send(data, data.Length);
        }
        catch (Exception e)
        {
            LogDebug($"Send UDP error: {e.Message}");
        }
    }
    
    private void SendHeartbeat()
    {
        var heartbeatMessage = new Dictionary<string, object>
        {
            { "type", "HEARTBEAT" }
        };
        
        SendTcpMessage(heartbeatMessage);
    }
    
    private void SendPing()
    {
        var pingMessage = new Dictionary<string, object>
        {
            { "type", "PING" },
            { "timestamp", DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond }
        };
        
        SendTcpMessage(pingMessage);
        _lastPingTime = Time.time;
    }
    
    public void Disconnect()
    {
        if (!_isConnected) return;
        
        try
        {
            // Send disconnect message
            if (_tcpClient != null && _tcpClient.Connected)
            {
                var disconnectMessage = new Dictionary<string, object>
                {
                    { "type", "DISCONNECT" }
                };
                
                // Fire and forget - don't await
                SendTcpMessage(disconnectMessage);
            }
        }
        catch (Exception)
        {
            // Ignore errors when trying to send disconnect
        }
        
        // Cancel ongoing operations
        _cts?.Cancel();
        
        // Close connections
        _tcpClient?.Close();
        _udpClient?.Close();
        
        // Reset state
        _isConnected = false;
        _currentRoomId = null;
        
        OnDisconnected?.Invoke("Disconnected from server");
        LogDebug("Disconnected from server");
    }
    
    // Game-specific methods
    public void HostGame(string roomName, int maxPlayers = 20)
    {
        if (!_isConnected) 
        {
            Debug.LogError("Cannot host game: Not connected to server");
            return;
        }
        
        Debug.Log($"Sending HOST_GAME request for room: {roomName}, maxPlayers: {maxPlayers}");
        
        var message = new Dictionary<string, object>
        {
            { "type", "HOST_GAME" },
            { "room_name", roomName },
            { "max_players", maxPlayers }
        };
        
        SendTcpMessage(message);
    }
    
    public void JoinGame(string roomId)
    {
        if (!_isConnected) return;
        
        var message = new Dictionary<string, object>
        {
            { "type", "JOIN_GAME" },
            { "room_id", roomId }
        };
        
        SendTcpMessage(message);
    }
    
    public void StartGame()
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        var message = new Dictionary<string, object>
        {
            { "type", "START_GAME" },
            { "room_id", _currentRoomId }
        };
        
        SendTcpMessage(message);
    }
    
    public void LeaveGame()
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        var message = new Dictionary<string, object>
        {
            { "type", "LEAVE_ROOM" },
            { "room_id", _currentRoomId }
        };
        
        SendTcpMessage(message);
        _currentRoomId = null;
        _isHost = false;
    }
    
    public void RequestRoomList()
    {
        if (!_isConnected) return;
        
        var message = new Dictionary<string, object>
        {
            { "type", "LIST_GAMES" }
        };
        
        SendTcpMessage(message);
    }
    
    public void SendPlayerState(GameManager.PlayerStateData stateData)
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        var message = new Dictionary<string, object>
        {
            ["type"] = "GAME_DATA",
            ["client_id"] = _clientId,
            ["room_id"] = _currentRoomId,
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
        
        SendUdpMessage(message);
    }
    
    public void SendPlayerInput(GameManager.PlayerInputData input)
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        var message = new Dictionary<string, object>
        {
            { "type", "GAME_DATA" },
            { "client_id", _clientId },
            { "room_id", _currentRoomId },
            { "data", new Dictionary<string, object>
                {
                    { "type", "PLAYER_INPUT" },
                    { "input", new Dictionary<string, object>
                        {
                            { "steering", input.steering },
                            { "throttle", input.throttle },
                            { "brake", input.brake },
                            { "timestamp", input.timestamp }
                        }
                    }
                }
            }
        };
        
        SendUdpMessage(message);
    }
    
    // Utility methods
    public string GetClientId() => _clientId;
    public string GetCurrentRoomId() => _currentRoomId;
    public float GetLatency() => _latency;
    public bool IsConnected() => _isConnected;
    public bool IsHost() => _isHost;
    
    private void LogDebug(string message)
    {
        if (showDebugMessages)
            Debug.Log($"[NetworkManager] {message}");
    }

    // Add this method to check event subscriptions
    public bool HasGameHostedSubscribers()
    {
        return OnGameHosted != null;
    }
}