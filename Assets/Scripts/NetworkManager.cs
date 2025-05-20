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
    public int tcpPort = 8443;
    public int udpPort = 8443;
    public float heartbeatInterval = 5f;
    
    [Header("Authentication")]
    public bool rememberCredentials = true;
    public string defaultPlayerName = "Player";
    private string playerPassword = ""; // Will be set during registration
    
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
    private string _deviceName = "Unknown Device";
    
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
            
            // Capture device name on main thread
            _deviceName = SystemInfo.deviceName;
            
            // Load saved credentials if available
            LoadSavedCredentials();
            
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
            Debug.Log("Sending scene ready notification after scene change");
            
            // According to SERVER-README.md, SCENE_READY needs to be sent to each player individually
            // We can't use "all" as targetId - instead we'll broadcast to room via GameManager
            // Use BroadcastSceneReady method which is already set up correctly
            if (GameManager.Instance != null)
            {
                GameManager.Instance.BroadcastSceneReady();
                Debug.Log($"Broadcasted scene ready state via GameManager");
            }
            else
            {
                Debug.LogError("Cannot broadcast SCENE_READY - GameManager instance is null");
            }
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
            
            // Start listening tasks before sending any data
            _ = Task.Run(ListenForTcpMessages, _cts.Token);
            _ = Task.Run(ListenForUdpMessages, _cts.Token);
            
            // Reset buffer
            _messageBuffer.Clear();
            
            // Note: Don't set _isConnected = true here
            // It will be set when we receive the connection confirmation
            
            LogDebug("Socket connection established, waiting for server confirmation");
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
    
    // Fix SendRegistration to use NAME command according to section 3.2 of SERVER-README.md
    private async Task SendRegistration()
    {
        // For clarity, let's check if we have a password first
        bool hasPassword = !string.IsNullOrEmpty(playerPassword);
        
        // First attempt - send just the name to see if we need authentication
        var registrationMessage = new Dictionary<string, object>
        {
            { "command", "NAME" },
            { "name", _deviceName }
        };
        
        // Only add password if we have one
        if (hasPassword)
        {
            registrationMessage["password"] = playerPassword;
            Debug.Log($"Sending player registration with name: {_deviceName} and password");
        }
        else
        {
            Debug.Log($"Sending player registration with name: {_deviceName} (no password)");
            
            // If we don't have a password, prepare to show auth panel after server response
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                // Don't show auth panel immediately, wait for server response
                // If name is new, server will accept without password
                // If name exists, server will send AUTH_FAILED
            });
        }
        
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
                Debug.Log($"Received raw data: {data}");
                _messageBuffer.Append(data);
                
                string messages = _messageBuffer.ToString();
                
                // Check for welcome message format: "CONNECTED|<sessionId>\n"
                if (messages.StartsWith("CONNECTED|"))
                {
                    int newlineIndex = messages.IndexOf('\n');
                    if (newlineIndex != -1)
                    {
                        string welcomeMsg = messages.Substring(0, newlineIndex);
                        string[] parts = welcomeMsg.Split('|');
                        if (parts.Length == 2)
                        {
                            _clientId = parts[1];
                            _isConnected = true;
                            LogDebug($"Connected with session ID: {_clientId}");
                            
                            // Notify on the main thread that we're connected
                            await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => {
                                // Now send the player name
                                SendRegistration();
                                OnConnected?.Invoke("Connected successfully");
                            });
                        }
                        
                        // Remove welcome message from buffer
                        messages = messages.Substring(newlineIndex + 1);
                        _messageBuffer.Clear();
                        _messageBuffer.Append(messages);
                    }
                }
                
                // Process any remaining JSON messages
                int newlineIndex2;
                while ((newlineIndex2 = messages.IndexOf('\n')) != -1)
                {
                    string message = messages.Substring(0, newlineIndex2);
                    messages = messages.Substring(newlineIndex2 + 1);
                    
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
            
            var messageObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
            if (messageObj == null)
            {
                Debug.LogError("Failed to parse message as JSON");
                return;
            }
            
            // Server uses "command" as the key
            if (!messageObj.ContainsKey("command"))
            {
                Debug.LogError("Message missing 'command' property");
                return;
            }
            
            string messageType = messageObj["command"].ToString();
            Debug.Log($"Processing JSON message of type: {messageType}");
            
            // Handle messages based on command
            switch (messageType)
            {
                case "NAME_OK":
                    LogDebug($"Name acknowledged by server");
                    
                    // Check if we're authenticated
                    bool authenticated = false;
                    if (messageObj.ContainsKey("authenticated"))
                    {
                        if (messageObj["authenticated"] is bool authBool)
                        {
                            authenticated = authBool;
                        }
                        else if (messageObj["authenticated"] is string authStr)
                        {
                            bool.TryParse(authStr, out authenticated);
                        }
                    }
                    
                    Debug.Log($"Authentication status: {(authenticated ? "Authenticated" : "Not authenticated")}");
                    
                    // Save credentials if successfully authenticated
                    if (authenticated && rememberCredentials)
                    {
                        SaveCredentials(_deviceName, playerPassword);
                        
                        // Continue with normal flow
                        UnityMainThreadDispatcher.Instance().Enqueue(() => {
                            if (UIManager.Instance != null)
                            {
                                UIManager.Instance.HideConnectionPanel();
                                UIManager.Instance.ShowNotification("Successfully authenticated!");
                            }
                        });
                    }
                    else if (!authenticated)
                    {
                        // Show auth panel if authentication failed
                        UnityMainThreadDispatcher.Instance().Enqueue(() => {
                            if (UIManager.Instance != null)
                            {
                                UIManager.Instance.HideConnectionPanel();
                                UIManager.Instance.ShowAuthPanel("Please enter password for this username");
                            }
                        });
                    }
                    
                    break;
                    
                case "AUTH_FAILED":
                    string errorMessage = "Authentication failed";
                    if (messageObj.ContainsKey("message"))
                    {
                        errorMessage = messageObj["message"].ToString();
                    }
                    
                    Debug.LogError($"Authentication error: {errorMessage}");
                    
                    // Show auth panel with error message
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        if (UIManager.Instance != null)
                        {
                            UIManager.Instance.HideConnectionPanel();
                            UIManager.Instance.ShowAuthPanel(errorMessage);
                        }
                    });
                    
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
                        
                        Debug.Log($"Successfully created room: {roomCreatedMsg["room_name"]} with ID: {roomCreatedMsg["room_id"]}");
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnGameHosted?.Invoke(roomCreatedMsg));
                        
                        LogDebug($"Room created: {_currentRoomId}, {messageObj["name"]}");
                    }
                    break;
                    
                case "ROOM_LIST":
                    Debug.Log($"Room list received: {message}");
                    
                    // Convert the server's room list format to our internal format
                    var roomListMsg = new Dictionary<string, object>();
                    var roomsList = new List<Dictionary<string, object>>();
                    
                    if (messageObj.ContainsKey("rooms"))
                    {
                        // Extract the rooms array more carefully
                        try
                        {
                            // Check different possible types for the rooms property
                            if (messageObj["rooms"] is Newtonsoft.Json.Linq.JArray jArray)
                            {
                                // It's already a JArray
                                foreach (var roomObj in jArray)
                                {
                                    var serverRoom = roomObj.ToObject<Dictionary<string, object>>();
                                    AddRoomToList(roomsList, serverRoom);
                                }
                            }
                            else if (messageObj["rooms"] is IEnumerable<object> enumerable)
                            {
                                // It's a generic enumerable
                                foreach (var roomObj in enumerable)
                                {
                                    if (roomObj is Newtonsoft.Json.Linq.JObject jObject)
                                    {
                                        var serverRoom = jObject.ToObject<Dictionary<string, object>>();
                                        AddRoomToList(roomsList, serverRoom);
                                    }
                                }
                            }
                            else
                            {
                                // Try to deserialize it directly
                                string roomsJson = messageObj["rooms"].ToString();
                                var directRooms = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(roomsJson);
                                if (directRooms != null)
                                {
                                    foreach (var serverRoom in directRooms)
                                    {
                                        AddRoomToList(roomsList, serverRoom);
                                    }
                                }
                                else
                                {
                                    Debug.LogError($"Could not parse rooms data: {roomsJson}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error parsing rooms data: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogError("Server response missing 'rooms' array");
                    }
                    
                    roomListMsg["rooms"] = roomsList;
                    
                    Debug.Log($"Final processed room list: {JsonConvert.SerializeObject(roomListMsg)}");
                    
                    UnityMainThreadDispatcher.Instance().Enqueue(() => 
                        OnRoomListReceived?.Invoke(roomListMsg));
                    break;
                    
                case "JOIN_OK":
                    if (messageObj.ContainsKey("roomId"))
                    {
                        _currentRoomId = messageObj["roomId"].ToString();
                        
                        // According to SERVER-README.md section 3.2, store the host ID if provided
                        if (messageObj.ContainsKey("hostId"))
                        {
                            _hostId = messageObj["hostId"].ToString();
                            _isHost = (_hostId == _clientId);
                            Debug.Log($"Room host is: {_hostId}, local client is: {_clientId}, isHost: {_isHost}");
                        }
                        
                        var joinedMsg = new Dictionary<string, object>
                        {
                            { "room_id", _currentRoomId },
                            { "host_id", _hostId ?? _clientId }
                        };
                        
                        Debug.Log($"Successfully joined room with ID: {_currentRoomId}");
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnRoomJoined?.Invoke(joinedMsg));
                        
                        LogDebug($"Joined room: {_currentRoomId}");
                    }
                    break;
                    
                case "ROOM_PLAYERS":
                    Debug.Log($"Room players list received: {message}");
                    
                    // Check if the message contains the players array
                    if (messageObj.ContainsKey("players"))
                    {
                        var roomPlayersMsg = new Dictionary<string, object>();
                        
                        // Copy the original message fields
                        foreach (var kvp in messageObj)
                        {
                            roomPlayersMsg[kvp.Key] = kvp.Value;
                        }
                        
                        Debug.Log($"Forwarding players list with {roomPlayersMsg.Count} fields");
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnRoomPlayersReceived?.Invoke(roomPlayersMsg));
                    }
                    break;
                    
                case "GAME_STARTED":
                    Debug.Log($"Game started message received: {message}");
                    
                    // According to SERVER-README.md section 7.3, the GAME_STARTED response format should include spawn positions:
                    // {"command":"GAME_STARTED","roomId":"roomId","hostId":"hostId","spawnPositions":{"playerId1":{"x":66,"y":-2,"z":0.8},"playerId2":{"x":60,"y":-2,"z":0.8}}}
                    
                    var gameStartedMsg = new Dictionary<string, object>();
                    
                    // Check if server provided spawn positions
                    if (messageObj.ContainsKey("spawnPositions"))
                    {
                        Debug.Log("Server provided spawn positions, using those");
                        
                        // Extract spawn position for this player
                        var spawnPositions = messageObj["spawnPositions"] as Newtonsoft.Json.Linq.JObject;
                        if (spawnPositions != null && spawnPositions.ContainsKey(_clientId))
                        {
                            var mySpawnPos = spawnPositions[_clientId] as Newtonsoft.Json.Linq.JObject;
                            if (mySpawnPos != null)
                            {
                                // Use server-assigned spawn position
                                gameStartedMsg["spawn_position"] = mySpawnPos;
                                Debug.Log($"Using server-assigned spawn position: {mySpawnPos["x"]},{mySpawnPos["y"]},{mySpawnPos["z"]} for player {_clientId}");
                            }
                        }
                    }
                    
                    // If no position found or provided, use a default position based on predefined garage positions
                    if (!gameStartedMsg.ContainsKey("spawn_position"))
                    {
                        Debug.Log("No spawn position found in server message, using fallback position");
                        
                        // Find an appropriate fallback position using predefined garage positions from section 7.3
                        // The track garage positions are defined in the GameManager class
                        var spawnPosObj = new Newtonsoft.Json.Linq.JObject();
                        spawnPosObj["x"] = 66f;  // Position 0 from documentation
                        spawnPosObj["y"] = -2f;
                        spawnPosObj["z"] = 0.8f;
                        spawnPosObj["index"] = 0; // Default spawn index
                        
                        gameStartedMsg["spawn_position"] = spawnPosObj;
                        Debug.Log($"Using fallback spawn position: {spawnPosObj["x"]},{spawnPosObj["y"]},{spawnPosObj["z"]}");
                    }
                    
                    // Include room ID from server response
                    if (messageObj.ContainsKey("roomId"))
                    {
                        gameStartedMsg["room_id"] = messageObj["roomId"];
                    }
                    
                    // Include host ID if provided
                    if (messageObj.ContainsKey("hostId"))
                    {
                        gameStartedMsg["host_id"] = messageObj["hostId"];
                    }
                    
                    // Make sure we update the scene on the main thread
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        OnGameStarted?.Invoke(gameStartedMsg);
                        LogDebug($"Game started for room: {_currentRoomId}");
                    });
                    break;
                
                // Handle errors properly 
                case "UNKNOWN_COMMAND":
                    Debug.LogError($"Server rejected command: {messageObj["originalCommand"]}");
                    break;
                    
                case "ERROR":
                    Debug.LogError($"Server error: {messageObj["message"]}");
                    break;
                    
                case "RELAYED_MESSAGE":
                    Debug.Log($"Received relayed message: {message}");
                    
                    // According to SERVER-README.md section 3.2, RELAYED_MESSAGE has format:
                    // {"command":"RELAYED_MESSAGE","senderId":"id","senderName":"name","message":"text"}
                    
                    if (messageObj.ContainsKey("senderId") && messageObj.ContainsKey("message"))
                    {
                        var relayedMsg = new Dictionary<string, object>
                        {
                            { "sender_id", messageObj["senderId"] },
                            { "message", messageObj["message"] }
                        };
                        
                        // Include sender name if provided
                        if (messageObj.ContainsKey("senderName"))
                        {
                            relayedMsg["sender_name"] = messageObj["senderName"];
                        }
                        
                        // If this is a SCENE_READY message, handle it specially
                        string msgContent = messageObj["message"].ToString();
                        if (msgContent.StartsWith("SCENE_READY:"))
                        {
                            try
                            {
                                string[] parts = msgContent.Split(':');
                                if (parts.Length > 1)
                                {
                                    string readyPlayerId = parts[1];
                                    Debug.Log($"Player {readyPlayerId} is ready - scene loaded");
                                    
                                    // Notify GameManager that this player is ready
                                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                                        if (GameManager.Instance != null)
                                        {
                                            GameManager.Instance.HandlePlayerReady(readyPlayerId);
                                        }
                                    });
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"Error processing SCENE_READY message: {e.Message}");
                            }
                        }
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnRelayReceived?.Invoke(relayedMsg));
                    }
                    break;

                case "PLAYER_INFO":
                    // According to section 3.2, PLAYER_INFO response format:
                    // {"command":"PLAYER_INFO","playerInfo":{"id":"id","name":"playerName","currentRoomId":"roomId"}}
                    Debug.Log($"Received player info from server: {message}");
                    
                    // Forward the player info to UI manager if needed
                    if (messageObj.ContainsKey("playerInfo"))
                    {
                        var playerInfoMsg = new Dictionary<string, object>();
                        playerInfoMsg["player_info"] = messageObj["playerInfo"];
                        
                        UnityMainThreadDispatcher.Instance().Enqueue(() => 
                            OnServerMessage?.Invoke(playerInfoMsg));
                    }
                    break;

                case "PONG":
                    // According to SERVER-README.md section 3.2, PONG is the response to PING
                    // We can't directly calculate latency here because Time.time can only be called from main thread
                    // Instead, dispatch to main thread to calculate latency
                    UnityMainThreadDispatcher.Instance().Enqueue(() => {
                        _latency = Time.time - _lastPingTime;
                        LogDebug($"Received PONG response, latency: {_latency * 1000:F2}ms");
                    });
                    break;
                    
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
            Debug.Log($"Received UDP message: {jsonMessage}");
            var message = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
            
            // Skip if message is invalid
            if (message == null) return;
            
            // Based on SERVER-README.md, UDP packets use the UPDATE command format:
            // {"command":"UPDATE","sessionId":"id","position":{"x":0,"y":0,"z":0},"rotation":{"x":0,"y":0,"z":0,"w":1}}
            
            if (message.ContainsKey("command") && message["command"].ToString() == "UPDATE" && 
                message.ContainsKey("sessionId") && message.ContainsKey("position") && message.ContainsKey("rotation"))
            {
                string fromClientId = message["sessionId"].ToString();
                
                // Skip our own messages
                if (fromClientId == _clientId) return;
                
                var position = message["position"] as Newtonsoft.Json.Linq.JObject;
                var rotation = message["rotation"] as Newtonsoft.Json.Linq.JObject;
                
                if (position != null && rotation != null)
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        if (GameManager.Instance != null)
                        {
                            bool playerExists = GameManager.Instance.IsPlayerActive(fromClientId);
                            
                            // Create player state from position and rotation
                            Vector3 pos = new Vector3(
                                Convert.ToSingle(position["x"]),
                                Convert.ToSingle(position["y"]),
                                Convert.ToSingle(position["z"])
                            );
                            
                            Quaternion rot = new Quaternion(
                                Convert.ToSingle(rotation["x"]),
                                Convert.ToSingle(rotation["y"]),
                                Convert.ToSingle(rotation["z"]),
                                Convert.ToSingle(rotation["w"])
                            );
                            
                            var playerState = new GameManager.PlayerStateData
                            {
                                playerId = fromClientId,
                                position = pos,
                                rotation = rot,
                                velocity = Vector3.zero,  // Server doesn't provide velocity
                                angularVelocity = Vector3.zero, // Server doesn't provide angular velocity
                                timestamp = Time.time
                            };
                            
                            // If the player doesn't exist yet, spawn them
                            if (!playerExists)
                            {
                                GameManager.Instance.SpawnRemotePlayer(
                                    fromClientId, 
                                    playerState.position, 
                                    playerState.rotation
                                );
                            }
                            
                            // Update the player's state
                            GameManager.Instance.ApplyPlayerState(playerState, !playerExists);
                        }
                    });
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
            { "command", "PING" } // Using PING as heartbeat since server supports it
        };
        
        SendTcpMessage(heartbeatMessage);
    }
    
    // Fix SendPing to use PING command
    private void SendPing()
    {
        var pingMessage = new Dictionary<string, object>
        {
            { "command", "PING" }
        };
        
        SendTcpMessage(pingMessage);
        _lastPingTime = Time.time;
    }
    
    public void Disconnect()
    {
        if (!_isConnected) return;
        
        try
        {
            // According to SERVER-README.md section 3.2, BYE is a supported command
            if (_tcpClient != null && _tcpClient.Connected)
            {
                var disconnectMessage = new Dictionary<string, object>
                {
                    { "command", "BYE" }
                };
                
                // Fire and forget - don't await
                SendTcpMessage(disconnectMessage);
                Debug.Log("Sent BYE command to server");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending disconnect message: {e.Message}");
        }
        
        // Cancel ongoing operations
        _cts?.Cancel();
        
        // Close connections
        _tcpClient?.Close();
        _udpClient?.Close();
        
        // Reset state
        _isConnected = false;
        _currentRoomId = null;
        _hostId = null;
        _isHost = false;
        
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
        
        Debug.Log($"Sending CREATE_ROOM request for room: {roomName}");
        
        var message = new Dictionary<string, object>
        {
            { "command", "CREATE_ROOM" },
            { "name", roomName }
        };
        
        SendTcpMessage(message);
    }
    
    public void JoinGame(string roomId)
    {
        if (!_isConnected) 
        {
            Debug.LogError("Cannot join game: Not connected to server");
            return; 
        }
        
        Debug.Log($"Joining room with ID: {roomId}");
        
        var message = new Dictionary<string, object>
        {
            { "command", "JOIN_ROOM" },
            { "roomId", roomId }
        };
        
        SendTcpMessage(message);
    }
    
    // According to SERVER-README.md section 3.2, only the host can start the game
    // The format is: {"command":"START_GAME"} - no roomId needed since server knows player's room
    public void StartGame()
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) 
        {
            Debug.LogError("Cannot start game: Not connected or no room ID");
            return;
        }
        
        if (!_isHost)
        {
            Debug.LogError("Cannot start game: Only the host can start the game");
            return;
        }
        
        Debug.Log($"Sending START_GAME command for room: {_currentRoomId} as host: {_isHost}");
        
        var message = new Dictionary<string, object>
        {
            { "command", "START_GAME" }
        };
        
        SendTcpMessage(message);
    }
    
    public void LeaveGame()
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        // According to SERVER-README.md section 3.2, LEAVE_ROOM doesn't take a roomId parameter
        // {"command":"LEAVE_ROOM"}
        var message = new Dictionary<string, object>
        {
            { "command", "LEAVE_ROOM" }
        };
        
        SendTcpMessage(message);
        _currentRoomId = null;
        _isHost = false;
    }
    
    public void RequestRoomList()
    {
        if (!_isConnected) 
        {
            Debug.LogError("Cannot request room list: Not connected to server");
            return;
        }
        
        Debug.Log("Sending LIST_ROOMS request to server");
        
        var message = new Dictionary<string, object>
        {
            { "command", "LIST_ROOMS" }
        };
        
        SendTcpMessage(message);
    }
    
    public void SendPlayerState(GameManager.PlayerStateData stateData)
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        // According to SERVER-README.md section 4.2, the UDP format must be:
        // {"command":"UPDATE","sessionId":"id","position":{"x":0,"y":0,"z":0},"rotation":{"x":0,"y":0,"z":0,"w":1}}
        var message = new Dictionary<string, object>
        {
            ["command"] = "UPDATE",
            ["sessionId"] = _clientId,
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
            }
        };
        
        // Debug.Log($"Sending UDP state update: {JsonConvert.SerializeObject(message)}");
        SendUdpMessage(message);
    }
    
    // Make sure we properly format the UDP INPUT command according to documentation
    public void SendPlayerInput(GameManager.PlayerInputData input)
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        // According to SERVER-README.md section 4.2, the INPUT format must be:
        // {"command":"INPUT","sessionId":"id","roomId":"roomId","input":{...},"client_id":"id"}
        var message = new Dictionary<string, object>
        {
            { "command", "INPUT" },
            { "sessionId", _clientId },
            { "roomId", _currentRoomId },
            { "input", new Dictionary<string, object>
                {
                    { "steering", input.steering },
                    { "throttle", input.throttle },
                    { "brake", input.brake },
                    { "timestamp", input.timestamp }
                }
            },
            { "client_id", _clientId } // Required according to docs
        };
        
        // Debug.Log($"Sending UDP input: {JsonConvert.SerializeObject(message)}");
        SendUdpMessage(message);
    }
    
    // Fix GET_ROOM_PLAYERS to include roomId parameter as required in the documentation
    public void GetRoomPlayers(string roomId)
    {
        if (!_isConnected) return;
        
        // According to SERVER-README.md section 3.2, GET_ROOM_PLAYERS should include roomId
        var message = new Dictionary<string, object>
        {
            { "command", "GET_ROOM_PLAYERS" },
            { "roomId", roomId }
        };
        
        SendTcpMessage(message);
    }

    // Get player information from the server
    public void RequestPlayerInfo()
    {
        if (!_isConnected) return;
        
        // According to SERVER-README.md section 3.2, PLAYER_INFO command:
        // {"command":"PLAYER_INFO"}
        var message = new Dictionary<string, object>
        {
            { "command", "PLAYER_INFO" }
        };
        
        SendTcpMessage(message);
        Debug.Log("Sent PLAYER_INFO request to server");
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

    // Helper method to add a room to the room list with proper mapping
    private void AddRoomToList(List<Dictionary<string, object>> roomsList, Dictionary<string, object> serverRoom)
    {
        var roomData = new Dictionary<string, object>();
        
        // Match keys with our UI's expected format - according to SERVER-README.md field names
        roomData["room_id"] = serverRoom.ContainsKey("id") ? serverRoom["id"] : "";
        roomData["name"] = serverRoom.ContainsKey("name") ? serverRoom["name"] : "Unknown Room";
        roomData["player_count"] = serverRoom.ContainsKey("playerCount") ? serverRoom["playerCount"] : 0;
        roomData["max_players"] = 20; // Default to 20 if not provided
        roomData["is_active"] = serverRoom.ContainsKey("isActive") ? serverRoom["isActive"] : false;
        
        // Per section 3.2, store the hostId if provided, for host status determination later
        if (serverRoom.ContainsKey("hostId"))
        {
            roomData["host_id"] = serverRoom["hostId"];
        }
        
        roomsList.Add(roomData);
        Debug.Log($"Added room: {roomData["name"]} (ID: {roomData["room_id"]})");
    }

    // Helper method to get the room host ID
    public string GetRoomHostId()
    {
        // Return the stored host ID, or default to client ID if none is available
        return _hostId ?? _clientId;
    }

    // New method to set auth credentials
    public void SetCredentials(string playerName, string password)
    {
        _deviceName = playerName;
        playerPassword = password;
        
        // If already connected, send updated credentials
        if (_isConnected)
        {
            SendRegistration();
        }
        
        if (rememberCredentials)
        {
            SaveCredentials(playerName, password);
        }
    }
    
    // New methods to manage credentials
    private void SaveCredentials(string playerName, string password)
    {
        if (!rememberCredentials) return;
        
        try
        {
            PlayerPrefs.SetString("PlayerName", playerName);
            PlayerPrefs.SetString("PlayerPassword", password);
            PlayerPrefs.Save();
            Debug.Log("Saved player credentials");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save credentials: {e.Message}");
        }
    }
    
    private void LoadSavedCredentials()
    {
        if (!rememberCredentials) return;
        
        try
        {
            if (PlayerPrefs.HasKey("PlayerName"))
            {
                _deviceName = PlayerPrefs.GetString("PlayerName");
            }
            
            if (PlayerPrefs.HasKey("PlayerPassword"))
            {
                playerPassword = PlayerPrefs.GetString("PlayerPassword");
            }
            
            Debug.Log($"Loaded saved credentials for: {_deviceName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load credentials: {e.Message}");
        }
    }
    
    // New method for explicit authentication (if needed separately)
    public void Authenticate(string password)
    {
        if (!_isConnected) return;
        
        var authMessage = new Dictionary<string, object>
        {
            { "command", "AUTHENTICATE" },
            { "password", password }
        };
        
        playerPassword = password; // Store for future use
        SendTcpMessage(authMessage);
    }
}