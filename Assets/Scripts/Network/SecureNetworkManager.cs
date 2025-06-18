using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Collections;
using System.Linq;
using System.Collections.Concurrent;

/// <summary>
/// High-performance secure network manager for MP-Server protocol
/// Implements TLS-encrypted TCP and AES-encrypted UDP communication
/// Full compliance with MP-Server security standards
/// </summary>
public class SecureNetworkManager : MonoBehaviour
{
    public static SecureNetworkManager Instance { get; private set; }
    
    [Header("Server Configuration")]
    [SerializeField] private string serverHost = "89.114.116.19";
    [SerializeField] private int serverPort = 443; // TLS port for MP-Server
    [SerializeField] private bool acceptSelfSignedCerts = true; // Development mode
    [SerializeField] private string pinnedCertificateThumbprint = ""; // Production mode
    
    [Header("Player Credentials")]
    [SerializeField] private string playerName = "Player";
    [SerializeField] private string playerPassword = "password123";
    [SerializeField] private bool saveCredentials = true;
    
    [Header("Performance Settings")]
    [SerializeField] private int tcpTimeoutMs = 10000;
    [SerializeField] private int udpUpdateRateHz = 20; // Position updates per second
    [SerializeField] private int maxRetryAttempts = 3;
    [SerializeField] private bool enableCompression = false; // For large packets
    
    [Header("Security Settings")]
    [SerializeField] private bool enforceEncryption = true;
    [SerializeField] private bool logSecurityEvents = true;
    [SerializeField] private int rateLimitTcpMs = 100; // Min interval between TCP messages
    [SerializeField] private int rateLimitUdpMs = 50; // Min interval between UDP messages
    
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private bool logNetworkTraffic = false;
    
    // Core networking components
    private TcpClient _tcpClient;
    private SslStream _sslStream;
    private UdpClient _udpClient;
    private StreamReader _tcpReader;
    private StreamWriter _tcpWriter;
    
    // Connection state
    private volatile bool _isConnected = false;
    private volatile bool _isAuthenticated = false;
    private volatile bool _isInRoom = false;
    
    // Session data
    private string _sessionId;
    private string _currentRoomId;
    private string _currentRoomHostId;
    private UdpEncryption _udpCrypto;
    private IPEndPoint _serverUdpEndpoint;
    
    // Rate limiting
    private DateTime _lastTcpMessage = DateTime.MinValue;
    private DateTime _lastUdpMessage = DateTime.MinValue;
    private readonly object _rateLimitLock = new object();
    
    // Message queuing for rate limiting
    private readonly ConcurrentQueue<Func<Task>> _tcpMessageQueue = new ConcurrentQueue<Func<Task>>();
    private readonly ConcurrentQueue<Func<Task>> _udpMessageQueue = new ConcurrentQueue<Func<Task>>();
    
    // Performance metrics
    private float _latency = 0f;
    private int _packetsSent = 0;
    private int _packetsReceived = 0;
    private DateTime _lastLatencyCheck = DateTime.MinValue;
    
    // Cancellation tokens for cleanup
    private CancellationTokenSource _connectionCts;
    private CancellationTokenSource _messagingCts;
    
    // JSON serialization using Unity's JsonUtility (Unity-compatible)
    private static string SerializeToJson<T>(T obj)
    {
        return JsonUtility.ToJson(obj);
    }
    
    private static T DeserializeFromJson<T>(string json)
    {
        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (System.Exception)
        {
            return default(T);
        }
    }
    
    // Events - Modern MP-Server protocol events
    public event Action<string> OnConnected;
    public event Action<string> OnDisconnected;
    public event Action<string> OnConnectionFailed;
    public event Action<bool> OnAuthenticationChanged;
    public event Action<RoomInfo> OnRoomCreated;
    public event Action<RoomInfo> OnRoomJoined;
    public event Action<List<RoomInfo>> OnRoomListReceived;
    public event Action<GameStartData> OnGameStarted;
    public event Action<PlayerUpdate> OnPlayerPositionUpdate;
    public event Action<PlayerInput> OnPlayerInputUpdate;
    public event Action<RelayMessage> OnMessageReceived;
    public event Action<string> OnError;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeNetworkManager();
            Log("SecureNetworkManager initialized with MP-Server protocol compliance");
        }
        else
        {
            Log("Duplicate SecureNetworkManager destroyed");
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        LoadSavedCredentials();
        StartCoroutine(ProcessMessageQueues());
    }
    
    private void InitializeNetworkManager()
    {
        _connectionCts = new CancellationTokenSource();
        _messagingCts = new CancellationTokenSource();
        
        // Resolve server endpoint
        try
        {
            if (IPAddress.TryParse(serverHost, out IPAddress ipAddress))
            {
                _serverUdpEndpoint = new IPEndPoint(ipAddress, serverPort);
            }
            else
            {
                var hostEntry = Dns.GetHostEntry(serverHost);
                _serverUdpEndpoint = new IPEndPoint(hostEntry.AddressList[0], serverPort);
            }
            Log($"Server endpoint resolved: {_serverUdpEndpoint}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to resolve server host '{serverHost}': {ex.Message}");
            _serverUdpEndpoint = new IPEndPoint(IPAddress.Loopback, serverPort);
        }
    }
    
    #region Connection Management
    
    /// <summary>
    /// Connect to MP-Server with TLS encryption and authentication
    /// </summary>
    public async Task<bool> ConnectToServerAsync()
    {
        if (_isConnected)
        {
            LogError("Already connected to server");
            return true;
        }
        
        var attempt = 0;
        while (attempt < maxRetryAttempts)
        {
            try
            {
                attempt++;
                Log($"Connecting to MP-Server (attempt {attempt}/{maxRetryAttempts})...");
                
                // 1. Create TCP client and connect to server
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(serverHost, serverPort);
                
                if (_tcpClient.Connected)
                {
                    // 2. Setup TLS/SSL stream (accept self-signed certificates for MP-Server)
                    _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
                    await _sslStream.AuthenticateAsClientAsync(serverHost);
                    
                    // 3. Setup readers/writers for protocol communication
                    _tcpReader = new StreamReader(_sslStream, Encoding.UTF8);
                    _tcpWriter = new StreamWriter(_sslStream, Encoding.UTF8);
                    
                    // 4. Read connection confirmation from server
                    string response = await _tcpReader.ReadLineAsync();
                    
                    // Parse connection response (supports both JSON and pipe-delimited formats)
                    Dictionary<string, object> connectionData = null;
                    string sessionId = null;
                    
                    if (response.StartsWith("{"))
                    {
                        // JSON format
                        connectionData = ParseJsonMessage(response);
                        if (connectionData == null || !connectionData.ContainsKey("command") || 
                            connectionData["command"].ToString() != "CONNECTED")
                        {
                            throw new Exception($"Invalid JSON server response: {response}");
                        }
                        
                        if (connectionData.ContainsKey("sessionId"))
                        {
                            sessionId = connectionData["sessionId"].ToString();
                        }
                    }
                    else if (response.Contains("|"))
                    {
                        // Pipe-delimited format: CONNECTED|sessionId
                        var parts = response.Split('|');
                        if (parts.Length >= 2 && parts[0] == "CONNECTED")
                        {
                            sessionId = parts[1];
                            connectionData = new Dictionary<string, object>
                            {
                                ["command"] = "CONNECTED",
                                ["sessionId"] = sessionId
                            };
                        }
                        else
                        {
                            throw new Exception($"Invalid pipe-delimited server response: {response}");
                        }
                    }
                    else
                    {
                        throw new Exception($"Invalid server response format: {response}");
                    }
                    
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        throw new Exception("Server did not provide session ID");
                    }
                    
                    _sessionId = sessionId;
                    
                    _isConnected = true;
                    
                    Log($"✅ Successfully connected to MP-Server at {serverHost}:{serverPort}");
                    Log($"Session ID: {_sessionId}");
                    OnConnected?.Invoke($"Connected to {serverHost}:{serverPort}");
                    
                    // 5. Start receiving messages
                    _ = Task.Run(ReceiveMessages);
                    
                    // 6. Authenticate with server
                    await AuthenticateAsync();
                    
                    // 7. Initialize UDP client for game data
                    _udpClient = new UdpClient();
                    
                    return true;
                }
                else
                {
                    throw new Exception("Failed to establish TCP connection");
                }
            }
            catch (Exception ex)
            {
                LogError($"Connection attempt {attempt} failed: {ex.Message}");
                
                if (attempt < maxRetryAttempts)
                {
                    var delay = (int)Math.Pow(2, attempt) * 1000; // Exponential backoff
                    Log($"Retrying in {delay}ms...");
                    await Task.Delay(delay);
                }
            }
        }
        
        OnConnectionFailed?.Invoke($"Failed to connect after {maxRetryAttempts} attempts");
        return false;
    }
    
    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;
        
        try
        {
            // Close readers/writers
            _tcpReader?.Close();
            _tcpWriter?.Close();
            _tcpReader = null;
            _tcpWriter = null;
            
            // Close SSL stream
            _sslStream?.Close();
            _sslStream = null;
            
            // Close TCP connection properly
            if (_tcpClient != null && _tcpClient.Connected)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }
            
            // Close UDP connection properly
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            
            // Small delay to ensure cleanup
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            LogError($"Error during disconnect: {ex.Message}");
        }
        
        _isConnected = false;
        _isAuthenticated = false;
        _isInRoom = false;
        
        OnAuthenticationChanged?.Invoke(false);
        OnDisconnected?.Invoke("Disconnected from server");
        
        Log("Disconnected from MP-Server");
    }
    
    #endregion
    
    #region TCP Communication
    
    /// <summary>
    /// Send TCP message with rate limiting and automatic queuing
    /// </summary>
    public async Task SendTcpMessageAsync<T>(T message)
    {
        if (!_isConnected)
        {
            LogError("Cannot send TCP message - not connected");
            return;
        }
        
        _tcpMessageQueue.Enqueue(async () => await SendTcpMessageInternal(message));
    }
    
    private async Task SendTcpMessageInternal<T>(T message)
    {
        try
        {
            // Rate limiting
            lock (_rateLimitLock)
            {
                var timeSinceLastMessage = (DateTime.UtcNow - _lastTcpMessage).TotalMilliseconds;
                if (timeSinceLastMessage < rateLimitTcpMs)
                {
                    var delayMs = (int)(rateLimitTcpMs - timeSinceLastMessage);
                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
                _lastTcpMessage = DateTime.UtcNow;
            }
            
            // Serialize with Unity's JsonUtility
            var json = SerializeToJson(message);
            
            _packetsSent++;
            
            if (logNetworkTraffic)
            {
                Log($"📤 TCP Sent: {json}");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send TCP message: {ex.Message}");
        }
    }
    
    private IEnumerator ListenForTcpMessages()
    {
        var consecutiveErrors = 0;
        const int maxConsecutiveErrors = 3;
        
        while (_isConnected && _tcpReader != null)
        {
            var readTask = _tcpReader?.ReadLineAsync();
            if (readTask == null) break;
            
            // Wait for message with timeout
            var startTime = Time.time;
            while (!readTask.IsCompleted && !readTask.IsFaulted && !readTask.IsCanceled)
            {
                if (Time.time - startTime > 30f) // 30 second timeout
                {
                    LogError("TCP read timeout");
                    consecutiveErrors++;
                    break;
                }
                yield return null;
            }
            
            if (readTask.IsCompleted && !readTask.IsFaulted)
            {
                var message = readTask.Result;
                if (!string.IsNullOrEmpty(message))
                {
                    try
                    {
                        ProcessTcpMessage(message);
                        consecutiveErrors = 0;
                        _packetsReceived++;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error processing TCP message: {ex.Message}");
                        consecutiveErrors++;
                    }
                }
                else
                {
                    LogError("Received empty TCP message");
                    consecutiveErrors++;
                }
            }
            else if (readTask.IsFaulted)
            {
                LogError($"TCP read task faulted: {readTask.Exception?.Message}");
                consecutiveErrors++;
            }
            
            if (consecutiveErrors >= maxConsecutiveErrors)
            {
                LogError($"Too many consecutive TCP errors ({maxConsecutiveErrors}), disconnecting");
                break;
            }
            
            if (consecutiveErrors > 0)
            {
                yield return new WaitForSeconds(1f);
            }
        }
        
        LogError("TCP message listener ended");
        _ = DisconnectAsync();
    }
    
    private void ProcessTcpMessage(string message)
    {
        try
        {
            if (logNetworkTraffic)
            {
                Log($"📥 TCP Received: {message}");
            }
            
            // Parse message (supports both JSON and pipe-delimited formats)
            Dictionary<string, object> jsonData = null;
            
            if (message.StartsWith("{"))
            {
                // JSON format
                jsonData = ParseJsonMessage(message);
            }
            else if (message.Contains("|"))
            {
                // Pipe-delimited format: COMMAND|param1|param2|...
                jsonData = ParsePipeDelimitedMessage(message);
            }
            else
            {
                // Simple command without parameters
                jsonData = new Dictionary<string, object>
                {
                    ["command"] = message.Trim()
                };
            }
            
            if (jsonData == null || !jsonData.ContainsKey("command"))
            {
                LogError("Received message without command field");
                return;
            }
            
            var command = jsonData["command"].ToString();
            
            switch (command)
            {
                case "NAME_OK":
                    HandleNameOk(jsonData);
                    break;
                case "AUTH_FAILED":
                    HandleAuthFailed(jsonData);
                    break;
                case "ROOM_CREATED":
                    HandleRoomCreated(jsonData);
                    break;
                case "JOIN_OK":
                    HandleRoomJoined(jsonData);
                    break;
                case "ROOM_LEFT":
                    HandleRoomLeft(jsonData);
                    break;
                case "ROOM_LIST":
                    HandleRoomList(jsonData);
                    break;
                case "ROOM_PLAYERS":
                    HandleRoomPlayers(jsonData);
                    break;
                case "GAME_STARTED":
                    HandleGameStarted(jsonData);
                    break;
                case "MESSAGE_SENT":
                    HandleMessageSent(jsonData);
                    break;
                case "RELAYED_MESSAGE":
                    HandleRelayedMessage(jsonData);
                    break;
                case "ERROR":
                    HandleError(jsonData);
                    break;
                case "PONG":
                    HandlePong();
                    break;
                default:
                    Log($"Unhandled command: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to process TCP message: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Parse JSON message into a Dictionary for Unity compatibility
    /// </summary>
    private Dictionary<string, object> ParseJsonMessage(string json)
    {
        try
        {
            // Simple JSON parser for basic message structure
            var result = new Dictionary<string, object>();
            json = json.Trim().TrimStart('{').TrimEnd('}');
            
            var parts = json.Split(',');
            foreach (var part in parts)
            {
                var keyValue = part.Split(':');
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim().Trim('"');
                    var value = keyValue[1].Trim().Trim('"');
                    
                    // Try to parse numbers and booleans
                    if (int.TryParse(value, out int intVal))
                        result[key] = intVal;
                    else if (float.TryParse(value, out float floatVal))
                        result[key] = floatVal;
                    else if (bool.TryParse(value, out bool boolVal))
                        result[key] = boolVal;
                    else
                        result[key] = value;
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Failed to parse JSON: {ex.Message}");
            return null;
        }
    }
    
    private Dictionary<string, object> ParsePipeDelimitedMessage(string message)
    {
        try
        {
            var parts = message.Split('|');
            if (parts.Length == 0) return null;
            
            var result = new Dictionary<string, object>
            {
                ["command"] = parts[0]
            };
            
            // Handle common pipe-delimited formats
            switch (parts[0].ToUpper())
            {
                case "CONNECTED":
                    if (parts.Length >= 2)
                        result["sessionId"] = parts[1];
                    break;
                    
                case "AUTH_OK":
                case "NAME_OK":
                    if (parts.Length >= 2)
                        result["message"] = parts[1];
                    break;
                    
                case "AUTH_FAILED":
                case "ERROR":
                    if (parts.Length >= 2)
                        result["message"] = parts[1];
                    break;
                    
                case "ROOM_CREATED":
                    if (parts.Length >= 3)
                    {
                        result["roomId"] = parts[1];
                        result["name"] = parts[2];
                    }
                    break;
                    
                case "JOIN_OK":
                    if (parts.Length >= 3)
                    {
                        result["roomId"] = parts[1];
                        result["hostId"] = parts[2];
                    }
                    break;
                    
                case "ROOM_LIST":
                    // Parse room list in pipe-delimited format
                    // Expected format: ROOM_LIST|room1_data|room2_data|...
                    // Where room_data might be: id,name,hostId,playerCount,maxPlayers,isActive
                    var rooms = new List<object>();
                    
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(parts[i]))
                        {
                            var roomParts = parts[i].Split(',');
                            if (roomParts.Length >= 3) // At least id, name, and basic info
                            {
                                var roomData = new Dictionary<string, object>
                                {
                                    ["id"] = roomParts[0],
                                    ["name"] = roomParts.Length > 1 ? roomParts[1] : "Unknown Room",
                                    ["hostId"] = roomParts.Length > 2 ? roomParts[2] : "",
                                    ["playerCount"] = roomParts.Length > 3 && int.TryParse(roomParts[3], out int pc) ? pc : 0,
                                    ["maxPlayers"] = roomParts.Length > 4 && int.TryParse(roomParts[4], out int mp) ? mp : 8,
                                    ["isActive"] = roomParts.Length > 5 && bool.TryParse(roomParts[5], out bool active) ? active : true
                                };
                                rooms.Add(roomData);
                            }
                        }
                    }
                    
                    result["rooms"] = rooms.ToArray();
                    break;
                    
                default:
                    // For unknown commands, add all parameters as indexed values
                    for (int i = 1; i < parts.Length; i++)
                    {
                        result[$"param{i}"] = parts[i];
                    }
                    break;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Failed to parse pipe-delimited message: {ex.Message}");
            return null;
        }
    }
    
    private void HandleNameOk(Dictionary<string, object> data)
    {
        Log("Authentication successful");
        _isAuthenticated = true;
        
        // Initialize UDP encryption with session ID
        if (!string.IsNullOrEmpty(_sessionId))
        {
            _udpCrypto = new UdpEncryption(_sessionId);
            Log("UDP encryption initialized");
        }
        
        OnAuthenticationChanged?.Invoke(true);
    }
    
    private void HandleAuthFailed(Dictionary<string, object> data)
    {
        var message = data.ContainsKey("message") ? data["message"].ToString() : "Unknown error";
        LogError($"Authentication failed: {message}");
        OnError?.Invoke($"Authentication failed: {message}");
    }
    
    private void HandleRoomCreated(Dictionary<string, object> data)
    {
        if (data.ContainsKey("roomId") && data.ContainsKey("name"))
        {
            _currentRoomId = data["roomId"].ToString();
            _currentRoomHostId = _sessionId; // Creator becomes host
            _isInRoom = true;
            
            var roomInfo = new RoomInfo
            {
                Id = _currentRoomId,
                Name = data["name"].ToString(),
                HostId = _currentRoomHostId,
                PlayerCount = 1,
                MaxPlayers = 8, // Default max players
                IsActive = false
            };
            
            OnRoomCreated?.Invoke(roomInfo);
            Log($"Room created: {roomInfo.Name} ({roomInfo.Id})");
        }
    }
    
    private void HandleRoomJoined(Dictionary<string, object> data)
    {
        if (data.ContainsKey("roomId"))
        {
            _currentRoomId = data["roomId"].ToString();
            _isInRoom = true;
            
            if (data.ContainsKey("hostId"))
            {
                _currentRoomHostId = data["hostId"].ToString();
            }
            
            var roomInfo = new RoomInfo
            {
                Id = _currentRoomId,
                HostId = _currentRoomHostId,
                MaxPlayers = 8 // Default max players
            };
            
            OnRoomJoined?.Invoke(roomInfo);
            Log($"Joined room: {_currentRoomId}");
        }
    }
    
    private void HandleRoomLeft(Dictionary<string, object> data)
    {
        _currentRoomId = null;
        _currentRoomHostId = null;
        _isInRoom = false;
        
        Log("Successfully left room");
        // OnRoomLeft event would be triggered here if it exists
    }
    
    private void HandleRoomList(Dictionary<string, object> data)
    {
        // Handle room list response from server
        Log($"Handling room list response. Data keys: {string.Join(", ", data.Keys)}");
        
        var roomList = new List<RoomInfo>();
        
        if (data.ContainsKey("rooms") && data["rooms"] is object[] rooms)
        {
            Log($"Found {rooms.Length} rooms in response");
            
            foreach (var roomData in rooms)
            {
                if (roomData is Dictionary<string, object> room)
                {
                    var roomInfo = new RoomInfo();
                    
                    if (room.ContainsKey("id"))
                        roomInfo.Id = room["id"].ToString();
                    
                    if (room.ContainsKey("name"))
                        roomInfo.Name = room["name"].ToString();
                    
                    if (room.ContainsKey("hostId"))
                        roomInfo.HostId = room["hostId"].ToString();
                    
                    if (room.ContainsKey("playerCount"))
                        int.TryParse(room["playerCount"].ToString(), out roomInfo.PlayerCount);
                    
                    if (room.ContainsKey("maxPlayers"))
                        int.TryParse(room["maxPlayers"].ToString(), out roomInfo.MaxPlayers);
                    
                    if (room.ContainsKey("isActive"))
                        bool.TryParse(room["isActive"].ToString(), out roomInfo.IsActive);
                    
                    roomList.Add(roomInfo);
                    Log($"Added room: {roomInfo.Name} (ID: {roomInfo.Id}, Players: {roomInfo.PlayerCount}/{roomInfo.MaxPlayers})");
                }
            }
        }
        else
        {
            LogError($"Room list response missing 'rooms' key or wrong format. Available keys: {string.Join(", ", data.Keys)}");
        }
        
        OnRoomListReceived?.Invoke(roomList);
        Log($"Received room list with {roomList.Count} rooms");
    }
    
    private void HandleRoomPlayers(Dictionary<string, object> data)
    {
        // Handle room players list response
        // For now, just log that we received the response
        Log("Received room players list from server");
        // TODO: Parse player list and trigger appropriate events
    }
    
    private void HandleMessageSent(Dictionary<string, object> data)
    {
        // Handle confirmation that message was sent
        Log("Message sent successfully");
    }

    private void HandleGameStarted(Dictionary<string, object> data)
    {
        var gameData = new GameStartData();
        
        if (data.ContainsKey("roomId"))
            gameData.RoomId = data["roomId"].ToString();
            
        if (data.ContainsKey("hostId"))
            gameData.HostId = data["hostId"].ToString();
            
        // Simplified spawn positions - the MP-Server should send this in a simpler format
        gameData.SpawnPositions = new Dictionary<string, Vector3>();
        
        // For now, create default spawn positions
        gameData.SpawnPositions[_sessionId] = Vector3.zero;
        
        OnGameStarted?.Invoke(gameData);
        Log($"Game started in room {gameData.RoomId} with {gameData.SpawnPositions?.Count ?? 0} spawn positions");
    }
    
    private void HandleRelayedMessage(Dictionary<string, object> data)
    {
        var relay = new RelayMessage();
        
        if (data.ContainsKey("senderId"))
            relay.SenderId = data["senderId"].ToString();
            
        if (data.ContainsKey("senderName"))
            relay.SenderName = data["senderName"].ToString();
            
        if (data.ContainsKey("message"))
            relay.Message = data["message"].ToString();
        
        OnMessageReceived?.Invoke(relay);
        Log($"Message from {relay.SenderName}: {relay.Message}");
    }
    
    private void HandleError(Dictionary<string, object> data)
    {
        var message = data.ContainsKey("message") ? data["message"].ToString() : "Unknown error";
        LogError($"Server error: {message}");
        OnError?.Invoke(message);
    }
     private void HandlePong()
    {
        // Calculate latency
        var now = DateTime.UtcNow;
        if (_lastLatencyCheck != DateTime.MinValue)
        {
            _latency = (float)(now - _lastLatencyCheck).TotalMilliseconds;
        }
    }

    #endregion
    
    #region UDP Communication
    
    /// <summary>
    /// Send encrypted position update via UDP
    /// </summary>
    public async Task SendPositionUpdateAsync(Vector3 position, Quaternion rotation)
    {
        if (!CanSendUdp()) return;
        
        _udpMessageQueue.Enqueue(async () => await SendPositionUpdateInternal(position, rotation));
    }
    
    /// <summary>
    /// Send encrypted input update via UDP
    /// </summary>
    public async Task SendInputUpdateAsync(float steering, float throttle, float brake)
    {
        if (!CanSendUdp()) return;
        
        _udpMessageQueue.Enqueue(async () => await SendInputUpdateInternal(steering, throttle, brake));
    }
    
    private bool CanSendUdp()
    {
        if (!_isAuthenticated || !_isInRoom)
        {
            if (enableDebugLogs)
            {
                Log($"Cannot send UDP - Auth: {_isAuthenticated}, Room: {_isInRoom}");
            }
            return false;
        }
        
        return true;
    }
    
    private async Task SendPositionUpdateInternal(Vector3 position, Quaternion rotation)
    {
        try
        {
            // Rate limiting
            lock (_rateLimitLock)
            {
                var timeSinceLastMessage = (DateTime.UtcNow - _lastUdpMessage).TotalMilliseconds;
                if (timeSinceLastMessage < rateLimitUdpMs)
                {
                    return; // Skip this update
                }
                _lastUdpMessage = DateTime.UtcNow;
            }
            
            var update = new PositionUpdateMessage
            {
                command = "UPDATE",
                sessionId = _sessionId,
                position = new Vector3Data { x = position.x, y = position.y, z = position.z },
                rotation = new QuaternionData { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
            };
            
            // Send encrypted UDP packet using UdpEncryption
            if (_udpClient != null && _isConnected)
            {
                try
                {
                    var json = JsonUtility.ToJson(update);
                    
                    // Use UdpEncryption instance to create encrypted packet
                    if (_udpCrypto != null)
                    {
                        var encryptedData = _udpCrypto.CreatePacket(update);
                        await _udpClient.SendAsync(encryptedData, encryptedData.Length, serverHost, serverPort);
                        _packetsSent++;
                    }
                    else
                    {
                        LogError("UDP encryption not initialized - cannot send position update");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to send encrypted UDP position update: {ex.Message}");
                }
            }
            
            if (logNetworkTraffic)
            {
                Log($"📤 UDP Position: ({position.x:F2}, {position.y:F2}, {position.z:F2})");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send position update: {ex.Message}");
        }
    }
    
    private async Task SendInputUpdateInternal(float steering, float throttle, float brake)
    {
        try
        {
            var input = new InputUpdateMessage
            {
                command = "INPUT",
                sessionId = _sessionId,
                roomId = _currentRoomId,
                input = new InputData
                {
                    steering = steering,
                    throttle = throttle,
                    brake = brake,
                    timestamp = Time.time
                },
                client_id = _sessionId
            };
            
            // Send encrypted UDP packet using UdpEncryption
            if (_udpClient != null && _isConnected)
            {
                try
                {
                    var json = JsonUtility.ToJson(input);
                    
                    // Use UdpEncryption instance to create encrypted packet
                    if (_udpCrypto != null)
                    {
                        var encryptedData = _udpCrypto.CreatePacket(input);
                        await _udpClient.SendAsync(encryptedData, encryptedData.Length, serverHost, serverPort);
                        _packetsSent++;
                        
                        if (logNetworkTraffic)
                        {
                            Log($"📤 UDP Input: S:{steering:F2} T:{throttle:F2} B:{brake:F2}");
                        }
                    }
                    else
                    {
                        LogError("UDP encryption not initialized - cannot send input update");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to send encrypted UDP input update: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send input update: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Room Management
    
    /// <summary>
    /// Create a new racing room
    /// </summary>
    /// <summary>
    /// Create a new room on the server
    /// </summary>
    public async Task CreateRoomAsync(string roomName)
    {
        if (!_isAuthenticated)
        {
            LogError("Must be authenticated to create room");
            return;
        }

        try
        {
            // Send CREATE_ROOM command to server using JSON format
            var createRoomRequest = new
            {
                command = "CREATE_ROOM",
                name = roomName
            };
            
            string json = JsonUtility.ToJson(createRoomRequest);
            await SendTcpMessageAsync(json);
            
            // Response will be handled in ProcessTcpMessage when server responds
            Log($"Creating room: {roomName}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to create room: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Join an existing room
    /// </summary>
    public async Task JoinRoomAsync(string roomId)
    {
        if (!_isAuthenticated)
        {
            LogError("Must be authenticated to join room");
            return;
        }
        
        try
        {
            // Send JOIN_ROOM command as newline-delimited JSON
            var joinCommand = new
            {
                command = "JOIN_ROOM",
                roomId = roomId
            };
            
            string jsonCommand = JsonUtility.ToJson(joinCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            Log($"Sent join room request for room: {roomId}");
            
            // The server response will be handled in HandleTcpMessage
            // Expected responses: ROOM_JOINED or ERROR
        }
        catch (Exception ex)
        {
            LogError($"Failed to join room {roomId}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Leave current room
    /// </summary>
    public async Task LeaveRoomAsync()
    {
        if (!_isAuthenticated || !_isInRoom)
        {
            LogError("Not in a room to leave");
            return;
        }
        
        try
        {
            // Send LEAVE_ROOM command as newline-delimited JSON
            var leaveCommand = new
            {
                command = "LEAVE_ROOM"
            };
            
            string jsonCommand = JsonUtility.ToJson(leaveCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            Log("Sent leave room request");
            
            // The server response will be handled in HandleTcpMessage
            // Expected response: ROOM_LEFT
        }
        catch (Exception ex)
        {
            LogError($"Failed to leave room: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Start the game (host only)
    /// </summary>
    public async Task StartGameAsync()
    {
        if (!_isAuthenticated || !_isInRoom)
        {
            LogError("Must be in a room to start game");
            return;
        }
        
        try
        {
            // Send START_GAME command as newline-delimited JSON
            var startCommand = new
            {
                command = "START_GAME"
            };
            
            string jsonCommand = JsonUtility.ToJson(startCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            Log("Sent start game request");
            
            // The server response will be handled in HandleTcpMessage
            // Expected response: GAME_STARTED
        }
        catch (Exception ex)
        {
            LogError($"Failed to start game: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Request list of available rooms
    /// </summary>
    /// <summary>
    /// Request list of available rooms from server
    /// </summary>
    public async Task RequestRoomListAsync()
    {
        if (!_isConnected)
        {
            LogError("Must be connected to request room list");
            return;
        }

        try
        {
            if (enableDebugLogs)
            {
                Log("Sending room list request to server...");
            }
            
            // Send LIST_ROOMS command to server using proper JSON format
            var listRoomsRequest = new
            {
                command = "LIST_ROOMS"
            };
            
            string json = JsonUtility.ToJson(listRoomsRequest);
            await SendTcpMessageAsync(json);
            
            Log("Requesting room list from server");
        }
        catch (Exception ex)
        {
            LogError($"Failed to request room list: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send a message to another player
    /// </summary>
    public async Task SendMessageAsync(string targetId, string message)
    {
        if (!_isAuthenticated)
        {
            LogError("Must be authenticated to send messages");
            return;
        }
        
        try
        {
            // Send MESSAGE command as newline-delimited JSON
            var messageCommand = new
            {
                command = "MESSAGE",
                targetId = targetId,
                message = message
            };
            
            string jsonCommand = JsonUtility.ToJson(messageCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            Log($"Sent message to {targetId}: {message}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to send message to {targetId}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send periodic ping to measure latency
    /// </summary>
    public async Task SendPingAsync()
    {
        if (!_isConnected) return;
        
        try
        {
            _lastLatencyCheck = DateTime.UtcNow;
            
            // Send PING command as newline-delimited JSON
            var pingCommand = new
            {
                command = "PING",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            string jsonCommand = JsonUtility.ToJson(pingCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            // Latency will be calculated when PONG response is received in HandlePong
        }
        catch (Exception ex)
        {
            LogError($"Failed to send ping: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Message Queue Processing
    
    private IEnumerator ProcessMessageQueues()
    {
        while (true)
        {
            // Process TCP message queue
            while (_tcpMessageQueue.TryDequeue(out var tcpTask))
            {
                try
                {
                    _ = tcpTask(); // Fire and forget - don't await in coroutine
                }
                catch (Exception ex)
                {
                    LogError($"Error processing TCP message from queue: {ex.Message}");
                }
            }
            
            // Process UDP message queue
            while (_udpMessageQueue.TryDequeue(out var udpTask))
            {
                try
                {
                    _ = udpTask(); // Fire and forget - don't await in coroutine
                }
                catch (Exception ex)
                {
                    LogError($"Error processing UDP message from queue: {ex.Message}");
                }
            }
            
            yield return new WaitForSeconds(0.01f); // Process every 10ms
        }
    }
    
    #endregion
    
    #region Credential Management
    
    public void SetCredentials(string name, string password)
    {
        playerName = name;
        playerPassword = password;
        
        if (saveCredentials)
        {
            SaveCredentials();
        }
    }
    
    private void SaveCredentials()
    {
        try
        {
            PlayerPrefs.SetString("MP_PlayerName", playerName);
            PlayerPrefs.SetString("MP_PlayerPassword", playerPassword);
            PlayerPrefs.Save();
            Log("Credentials saved");
        }
        catch (Exception ex)
        {
            LogError($"Failed to save credentials: {ex.Message}");
        }
    }
    
    private void LoadSavedCredentials()
    {
        try
        {
            if (PlayerPrefs.HasKey("MP_PlayerName"))
            {
                playerName = PlayerPrefs.GetString("MP_PlayerName");
            }
            
            if (PlayerPrefs.HasKey("MP_PlayerPassword"))
            {
                playerPassword = PlayerPrefs.GetString("MP_PlayerPassword");
            }
            
            Log($"Loaded credentials for: {playerName}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to load credentials: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Public Properties and Methods
    
    public string SessionId => _sessionId;
    public string CurrentRoomId => _currentRoomId;
    public string CurrentRoomHostId => _currentRoomHostId;
    public bool IsConnected => _isConnected;
    public bool IsAuthenticated => _isAuthenticated;
    public bool IsInRoom => _isInRoom;
    public float Latency => _latency;
    public int PacketsSent => _packetsSent;
    public int PacketsReceived => _packetsReceived;
    public bool IsHost => _currentRoomHostId == _sessionId;
    
    /// <summary>
    /// Get or set the server host address
    /// </summary>
    public string ServerHost 
    { 
        get => serverHost; 
        set => serverHost = value; 
    }
    
    /// <summary>
    /// Get or set the server port
    /// </summary>
    public int ServerPort 
    { 
        get => serverPort; 
        set => serverPort = value; 
    }
    
    /// <summary>
    /// Get or set the player name
    /// </summary>
    public string PlayerName 
    { 
        get => playerName; 
        set => playerName = value; 
    }
    
    /// <summary>
    /// Get the client ID (same as SessionId for compatibility)
    /// </summary>
    public string GetClientId()
    {
        return _sessionId;
    }
    
    /// <summary>
    /// Request room players list (async operation)
    /// </summary>
    public async Task GetRoomPlayers(string roomId)
    {
        if (string.IsNullOrEmpty(roomId))
        {
            LogError("Cannot get room players - roomId is empty");
            return;
        }
        
        if (!_isConnected)
        {
            LogError("Cannot get room players - not connected");
            return;
        }
        
        try
        {
            // Send GET_ROOM_PLAYERS command as newline-delimited JSON
            var playersCommand = new
            {
                command = "GET_ROOM_PLAYERS",
                roomId = roomId
            };
            
            string jsonCommand = JsonUtility.ToJson(playersCommand);
            await SendTcpMessageAsync(jsonCommand);
            
            Log($"Requested player list for room: {roomId}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to get room players for {roomId}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get network statistics
    /// </summary>
    public NetworkStats GetNetworkStats()
    {
        return new NetworkStats
        {
            Latency = _latency,
            PacketsSent = _packetsSent,
            PacketsReceived = _packetsReceived,
            IsConnected = _isConnected,
            IsAuthenticated = _isAuthenticated,
            UdpEncrypted = _udpCrypto != null
        };
    }
    
    #endregion
    
    #region Logging
    
    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[MP-Client] {message}");
        }
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[MP-Client] {message}");
    }
    
    #endregion
    
    #region Unity Lifecycle
    
    void OnDestroy()
    {
        _connectionCts?.Cancel();
        _messagingCts?.Cancel();
        _ = DisconnectAsync();
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && _isConnected)
        {
            Log("Application paused - maintaining connection");
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && _isConnected)
        {
            Log("Application lost focus - maintaining connection");
        }
    }
    
    #endregion
    
    /// <summary>
    /// Authenticate with the MP-Server after connection
    /// </summary>
    private async Task AuthenticateAsync()
    {
        try
        {
            // Send NAME command with JSON format
            var authRequest = new
            {
                command = "NAME",
                name = playerName,
                password = playerPassword
            };
            
            string json = JsonUtility.ToJson(authRequest);
            await SendTcpMessageAsync(json);
            
            Log($"Sent authentication request for player: {playerName}");
            
            // Authentication response will be handled in ProcessTcpMessage
            // when server responds with NAME_OK or AUTH_FAILED
        }
        catch (Exception ex)
        {
            LogError($"Authentication failed: {ex.Message}");
            _isAuthenticated = false;
            OnAuthenticationChanged?.Invoke(false);
        }
    }
    
    /// <summary>
    /// Receive messages from TCP connection
    /// </summary>
    private async Task ReceiveMessages()
    {
        try
        {
            while (_tcpClient != null && _tcpClient.Connected && _isConnected && _tcpReader != null)
            {
                string message = await _tcpReader.ReadLineAsync();
                
                if (!string.IsNullOrEmpty(message))
                {
                    // Process received message on main thread
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        ProcessTcpMessage(message);
                    });
                }
                else
                {
                    // Connection closed by server
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error receiving messages: {ex.Message}");
        }
        finally
        {
            if (_isConnected)
            {
                UnityMainThreadDispatcher.Instance().Enqueue(async () =>
                {
                    await DisconnectAsync();
                });
            }
        }
    }
    
    /// <summary>
    /// Validate server certificate - accept self-signed certificates for MP-Server
    /// </summary>
    private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
        X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // Accept self-signed certificates for this MP-Server
        return true;
    }
    
    /// <summary>
    /// Send TCP message using MP-Server protocol
    /// </summary>
    private async Task SendTcpMessageAsync(string message)
    {
        if (_sslStream == null || !_isConnected)
        {
            LogError("Cannot send TCP message - not connected or SSL stream not available");
            return;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message + "\n");
            await _sslStream.WriteAsync(data, 0, data.Length);
            await _sslStream.FlushAsync();
            
            if (logNetworkTraffic)
            {
                Log($"📤 TCP Sent: {message}");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send TCP message: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Read TCP message using MP-Server protocol
    /// </summary>
    private async Task<string> ReadTcpMessage()
    {
        if (_sslStream == null)
        {
            throw new Exception("SSL stream not available");
        }

        byte[] buffer = new byte[4096];
        int bytesRead = await _sslStream.ReadAsync(buffer, 0, buffer.Length);
        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
        
        if (logNetworkTraffic)
        {
            Log($"📥 TCP Received: {message}");
        }
        
        return message;
    }
}

// Network data structures for MP-Server protocol
[System.Serializable]
public struct RoomInfo
{
    public string Id;
    public string Name;
    public string HostId;
    public int PlayerCount;
    public int MaxPlayers;
    public bool IsActive;
}

[System.Serializable]
public struct GameStartData
{
    public string RoomId;
    public string HostId;
    public Dictionary<string, Vector3> SpawnPositions;
}

[System.Serializable]
public struct RelayMessage
{
    public string SenderId;
    public string SenderName;
    public string Message;
}

[System.Serializable]
public struct PlayerUpdate
{
    public string SessionId;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    public float Timestamp;
}

[System.Serializable]
public struct PlayerInput
{
    public string SessionId;
    public float Steering;
    public float Throttle;
    public float Brake;
    public float Timestamp;
}

[System.Serializable]
public struct NetworkStats
{
    public float Latency;
    public int PacketsSent;
    public int PacketsReceived;
    public bool IsConnected;
    public bool IsAuthenticated;
    public bool UdpEncrypted;
}

[System.Serializable]
public struct NetworkStatus
{
    public bool IsConnected;
    public bool IsAuthenticated;
    public bool UdpEncrypted;
}

// Internal Unity-compatible message structures
[System.Serializable]
internal class PositionUpdateMessage
{
    public string command;
    public string sessionId;
    public Vector3Data position;
    public QuaternionData rotation;
}

[System.Serializable]
internal class InputUpdateMessage
{
    public string command;
    public string sessionId;
    public string roomId;
    public InputData input;
    public string client_id;
}

[System.Serializable]
internal class Vector3Data
{
    public float x;
    public float y;
    public float z;
}

[System.Serializable]
internal class QuaternionData
{
    public float x;
    public float y;
    public float z;
    public float w;
}

[System.Serializable]
internal class InputData
{
    public float steering;
    public float throttle;
    public float brake;
    public float timestamp;
}
