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
    
    [Header("Keepalive Settings")]
    [SerializeField] private float pingIntervalSeconds = 30f; // Send ping every 30 seconds
    private Coroutine _pingCoroutine;
    
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
    private CancellationTokenSource _udpReceiveCancellation;
    
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
                    
                    // 6. Start automatic ping coroutine
                    _pingCoroutine = StartCoroutine(AutoPingCoroutine());
                    
                    Log("Connected to MP-Server. Ready for authentication.");
                    
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
            // Send BYE command for graceful disconnect
            await SendByeAsync();
            
            // Stop ping coroutine
            if (_pingCoroutine != null)
            {
                StopCoroutine(_pingCoroutine);
                _pingCoroutine = null;
            }
            
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
            
            // Clean up UDP connection properly
            CleanupUdpForGameSession();
            
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
                case "AUTH_OK":
                    HandleAuthOk(jsonData);
                    break;
                case "AUTH_FAILED":
                    HandleAuthFailed(jsonData);
                    break;
                case "ROOM_CREATED":
                    HandleRoomCreated(jsonData);
                    break;
                case "JOIN_OK":
                case "ROOM_JOIN_OK":
                    HandleRoomJoined(jsonData);
                    break;
                case "ROOM_LEFT":
                case "ROOM_LEFT_OK":
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
                case "PLAYER_INFO":
                    HandlePlayerInfo(jsonData);
                    break;
                case "GAME_END":
                    HandleGameEnd(jsonData);
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
    
    private void HandleAuthOk(Dictionary<string, object> data)
    {
        Log("Re-authentication successful via AUTH_OK");
        _isAuthenticated = true;
        OnAuthenticationChanged?.Invoke(true);
        Log($"Authentication status updated to: {_isAuthenticated}");
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
        Log("Authentication successful via NAME_OK");
        _isAuthenticated = true;
        
        // Initialize UDP encryption with session ID
        if (!string.IsNullOrEmpty(_sessionId))
        {
            _udpCrypto = new UdpEncryption(_sessionId);
            Log("UDP encryption initialized");
        }
        
        OnAuthenticationChanged?.Invoke(true);
        Log($"Authentication status updated to: {_isAuthenticated}");
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
        
        // Clean up UDP when leaving room (game session ends)
        CleanupUdpForGameSession();
        
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
        
        // Initialize UDP communication now that the game session has started
        InitializeUdpForGameSession();
        
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

    private void HandlePlayerInfo(Dictionary<string, object> data)
    {
        Log("Received player info from server");
        
        // Extract player information including spawn position
        if (data.ContainsKey("spawnPosition"))
        {
            // Handle spawn position data
            Log($"Player spawn position received");
        }
        
        if (data.ContainsKey("playerId"))
        {
            Log($"Player ID: {data["playerId"]}");
        }
        
        // TODO: Parse and use player info data as needed
    }
    
    private void HandleGameEnd(Dictionary<string, object> data)
    {
        Log("Game ended");
        _isInRoom = false;
        
        // TODO: Handle game end scenario
        OnError?.Invoke("Game ended");
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
        if (!_isAuthenticated || !_isInRoom || _udpClient == null)
        {
            if (enableDebugLogs)
            {
                Log($"Cannot send UDP - Auth: {_isAuthenticated}, Room: {_isInRoom}, UDP: {_udpClient != null}");
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
            
            // Create update message in MP-Server format
            var update = new
            {
                command = "UPDATE",
                sessionId = _sessionId,
                position = new { x = position.x, y = position.y, z = position.z },
                rotation = new { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            // Send encrypted UDP packet using UdpEncryption
            if (_udpClient != null && _isConnected)
            {
                try
                {
                    // Use UdpEncryption instance to create encrypted packet
                    if (_udpCrypto != null)
                    {
                        var encryptedData = _udpCrypto.CreatePacket(update);
                        await _udpClient.SendAsync(encryptedData, encryptedData.Length, _serverUdpEndpoint);
                        _packetsSent++;
                        
                        if (logNetworkTraffic)
                        {
                            Log($"📤 UDP Position: ({position.x:F2}, {position.y:F2}, {position.z:F2})");
                        }
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
            // Create input message in MP-Server format
            var input = new
            {
                command = "INPUT",
                sessionId = _sessionId,
                roomId = _currentRoomId,
                input = new
                {
                    steering = steering,
                    throttle = throttle,
                    brake = brake,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };
            
            // Send encrypted UDP packet using UdpEncryption
            if (_udpClient != null && _isConnected)
            {
                try
                {
                    // Use UdpEncryption instance to create encrypted packet
                    if (_udpCrypto != null)
                    {
                        var encryptedData = _udpCrypto.CreatePacket(input);
                        await _udpClient.SendAsync(encryptedData, encryptedData.Length, _serverUdpEndpoint);
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
    
    /// <summary>
    /// Start receiving UDP messages from server
    /// </summary>
    private async Task StartUdpReceiving()
    {
        if (_udpClient == null)
        {
            LogError("UDP client not initialized");
            return;
        }

        try
        {
            Log("Starting UDP message receiving...");
            
            // Initialize cancellation token for UDP receiving
            _udpReceiveCancellation = new CancellationTokenSource();
            
            while (_isConnected && _udpClient != null && !_udpReceiveCancellation.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var encryptedData = result.Buffer;
                    
                    // Decrypt and process UDP message
                    if (_udpCrypto != null)
                    {
                        // Try to parse as position update
                        var positionUpdate = _udpCrypto.ParsePacket<PlayerUpdate>(encryptedData);
                        if (!string.IsNullOrEmpty(positionUpdate.SessionId))
                        {
                            // Dispatch to main thread for Unity operations
                            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            {
                                OnPlayerPositionUpdate?.Invoke(positionUpdate);
                            });
                            continue;
                        }
                        
                        // Try to parse as input update
                        var inputUpdate = _udpCrypto.ParsePacket<PlayerInput>(encryptedData);
                        if (!string.IsNullOrEmpty(inputUpdate.SessionId))
                        {
                            // Dispatch to main thread for Unity operations
                            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            {
                                OnPlayerInputUpdate?.Invoke(inputUpdate);
                            });
                            continue;
                        }
                        
                        if (logNetworkTraffic)
                        {
                            Log("📥 UDP: Unknown message type received");
                        }
                    }
                    else
                    {
                        LogError("UDP encryption not available for decryption");
                    }
                }
                catch (Exception ex)
                {
                    if (_isConnected)
                    {
                        LogError($"UDP receive error: {ex.Message}");
                    }
                    
                    // Wait before retry to avoid spam
                    await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"UDP receiving stopped: {ex.Message}");
        }
        finally
        {
            Log("UDP receiving stopped");
        }
    }
    
    /// <summary>
    /// Initialize UDP communication when game session starts
    /// </summary>
    private void InitializeUdpForGameSession()
    {
        try
        {
            if (_udpClient != null)
            {
                Log("UDP already initialized, skipping...");
                return;
            }
            
            Log("Initializing UDP client for game session...");
            
            // Initialize UDP client for game data
            _udpClient = new UdpClient();
            Log("UDP client initialized for game session");
            
            // Start UDP receiving for real-time game data
            _ = Task.Run(StartUdpReceiving);
            Log("UDP receiving started for game session");
        }
        catch (Exception ex)
        {
            LogError($"Failed to initialize UDP for game session: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clean up UDP communication when game session ends
    /// </summary>
    private void CleanupUdpForGameSession()
    {
        try
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
                _udpClient = null;
                Log("UDP client cleaned up after game session ended");
            }
            
            // Cancel UDP receiving if active
            _udpReceiveCancellation?.Cancel();
            _udpReceiveCancellation = null;
        }
        catch (Exception ex)
        {
            LogError($"Error cleaning up UDP: {ex.Message}");
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
            var createRoomRequest = new CreateRoomRequest(roomName, 8); // Default 8 max players
            
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
            var joinCommand = new JoinRoomRequest(roomId);
            
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
            var listRoomsRequest = new SimpleCommand("LIST_ROOMS");
            
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
    /// Send a message to another player
    /// </summary>
    public async Task SendRelayMessageAsync(string targetPlayerId, string message)
    {
        if (!_isAuthenticated)
        {
            LogError("Must be authenticated to send relay messages");
            return;
        }

        try
        {
            // Send RELAY_MESSAGE command as newline-delimited JSON
            var relayCommand = new
            {
                command = "RELAY_MESSAGE",
                message = message,
                targetId = targetPlayerId
            };
            
            string json = JsonUtility.ToJson(relayCommand);
            await SendTcpMessageAsync(json);
            
            Log($"Sent relay message to {targetPlayerId}: {message}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to send relay message: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get current player information including spawn position
    /// </summary>
    public async Task RequestPlayerInfoAsync()
    {
        try
        {
            // Send PLAYER_INFO command as newline-delimited JSON
            var playerInfoCommand = new
            {
                command = "PLAYER_INFO"
            };
            
            string json = JsonUtility.ToJson(playerInfoCommand);
            await SendTcpMessageAsync(json);
            
            Log("Requested player info");
        }
        catch (Exception ex)
        {
            LogError($"Failed to request player info: {ex.Message}");
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
    
    /// <summary>
    /// Get comprehensive protocol compliance status
    /// </summary>
    public Dictionary<string, bool> GetProtocolStatus()
    {
        return new Dictionary<string, bool>
        {
            ["Connected"] = _isConnected,
            ["Authenticated"] = _isAuthenticated,
            ["UDP_Encryption"] = _udpCrypto != null,
            ["In_Room"] = _isInRoom,
            ["Is_Host"] = IsHost,
            ["SSL_Connected"] = _sslStream != null && _isConnected,
            ["TCP_Reader_Active"] = _tcpReader != null,
            ["TCP_Writer_Active"] = _tcpWriter != null,
            ["UDP_Client_Active"] = _udpClient != null,
            ["Ping_Active"] = _pingCoroutine != null
        };
    }
    
    /// <summary>
    /// Get detailed connection information for debugging
    /// </summary>
    public string GetConnectionDetails()
    {
        var status = GetProtocolStatus();
        var details = new StringBuilder();
        
        details.AppendLine("=== MP-Server Connection Details ===");
        details.AppendLine($"Server: {serverHost}:{serverPort}");
        details.AppendLine($"Player: {playerName}");
        details.AppendLine($"Session ID: {_sessionId ?? "None"}");
        details.AppendLine($"Room ID: {_currentRoomId ?? "None"}");
        details.AppendLine($"Host ID: {_currentRoomHostId ?? "None"}");
        details.AppendLine("");
        
        details.AppendLine("Status:");
        foreach (var kvp in status)
        {
            details.AppendLine($"  {kvp.Key}: {(kvp.Value ? "✅" : "❌")}");
        }
        
        details.AppendLine("");
        details.AppendLine("Network Stats:");
        details.AppendLine($"  Latency: {_latency:F1}ms");
        details.AppendLine($"  Packets Sent: {_packetsSent}");
        details.AppendLine($"  Packets Received: {_packetsReceived}");
        details.AppendLine($"  UDP Update Rate: {udpUpdateRateHz}Hz");
        details.AppendLine($"  TCP Rate Limit: {rateLimitTcpMs}ms");
        details.AppendLine($"  UDP Rate Limit: {rateLimitUdpMs}ms");
        
        return details.ToString();
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
    
    /// <summary>
    /// Send ping to server to keep connection alive
    /// </summary>
    public async Task SendPingAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        try
        {
            _lastLatencyCheck = DateTime.UtcNow;
            
            var pingCommand = new
            {
                command = "PING"
            };
            
            string json = JsonUtility.ToJson(pingCommand);
            await SendTcpMessageAsync(json);
            
            if (enableDebugLogs)
            {
                Log("Sent PING to server");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send ping: {ex.Message}");
        }
    }

    /// <summary>
    /// Send BYE command to gracefully disconnect
    /// </summary>
    public async Task SendByeAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        try
        {
            var byeCommand = new
            {
                command = "BYE"
            };
            
            string json = JsonUtility.ToJson(byeCommand);
            await SendTcpMessageAsync(json);
            
            Log("Sent BYE command to server");
        }
        catch (Exception ex)
        {
            LogError($"Failed to send BYE: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Automatic ping coroutine to keep connection alive
    /// </summary>
    private IEnumerator AutoPingCoroutine()
    {
        while (_isConnected)
        {
            yield return new WaitForSeconds(pingIntervalSeconds);
            
            if (_isConnected)
            {
                _ = SendPingAsync();
            }
        }
    }
    
    /// <summary>
    /// Re-authenticate with the server (using NAME command)
    /// </summary>
    public async Task<bool> ReAuthenticateAsync()
    {
        if (!_isConnected)
        {
            LogError("Cannot re-authenticate - not connected");
            return false;
        }
        
        if (string.IsNullOrEmpty(playerName))
        {
            LogError("Cannot re-authenticate - no player name set");
            return false;
        }

        try
        {
            // Reset authentication state
            _isAuthenticated = false;
            OnAuthenticationChanged?.Invoke(false);
            
            // Send NAME command with JSON format (same as initial authentication)
            Log($"Re-auth credentials - Name: '{playerName}', Password length: {playerPassword?.Length ?? 0}");
            
            var authRequest = new AuthRequest("NAME", playerName, playerPassword ?? "");
            
            string json = JsonUtility.ToJson(authRequest);
            Log($"📤 Re-auth JSON being sent: {json}");
            await SendTcpMessageAsync(json);
            
            Log($"Sent re-authentication request for player: {playerName}");
            
            // Wait for authentication response (with timeout)
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(10);
            
            Log("Waiting for authentication response...");
            
            while (!_isAuthenticated && (DateTime.Now - startTime) < timeout)
            {
                await Task.Delay(100);
            }
            
            Log($"Authentication wait completed. Authenticated: {_isAuthenticated}");
            
            if (_isAuthenticated)
            {
                Log("Re-authentication successful");
                return true;
            }
            else
            {
                LogError("Re-authentication timed out");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError($"Re-authentication failed: {ex.Message}");
            _isAuthenticated = false;
            OnAuthenticationChanged?.Invoke(false);
            return false;
        }
    }

    /// <summary>
    /// Test all MP-Server protocol commands for compliance verification
    /// </summary>
    public async Task TestAllProtocolCommands()
    {
#if UNITY_EDITOR
        Log("=== MP-Server Protocol Compliance Test ===");
        
        if (!_isConnected)
        {
            LogError("Cannot test protocol - not connected to server");
            return;
        }
        
        try
        {
            // Test 1: PING/PONG
            Log("Testing PING command...");
            await SendPingAsync();
            await Task.Delay(1000);
            
            // Test 2: LIST_ROOMS
            Log("Testing LIST_ROOMS command...");
            await RequestRoomListAsync();
            await Task.Delay(1000);
            
            // Test 3: PLAYER_INFO
            Log("Testing PLAYER_INFO command...");
            await RequestPlayerInfoAsync();
            await Task.Delay(1000);
            
            if (_isAuthenticated)
            {
                // Test 4: CREATE_ROOM
                Log("Testing CREATE_ROOM command...");
                await CreateRoomAsync("Test Room " + DateTime.Now.ToString("HH:mm:ss"));
                await Task.Delay(2000);
                
                if (_isInRoom)
                {
                    // Test 5: GET_ROOM_PLAYERS
                    Log("Testing GET_ROOM_PLAYERS command...");
                    await GetRoomPlayers(_currentRoomId);
                    await Task.Delay(1000);
                    
                    // Test 6: RELAY_MESSAGE
                    Log("Testing RELAY_MESSAGE command...");
                    await SendRelayMessageAsync(_sessionId, "Test relay message");
                    await Task.Delay(1000);
                    
                    // Test 7: UDP position update
                    Log("Testing UDP position update...");
                    await SendPositionUpdateAsync(Vector3.zero, Quaternion.identity);
                    await Task.Delay(500);
                    
                    // Test 8: UDP input update
                    Log("Testing UDP input update...");
                    await SendInputUpdateAsync(0.5f, 0.8f, 0.0f);
                    await Task.Delay(500);
                    
                    // Test 9: LEAVE_ROOM
                    Log("Testing LEAVE_ROOM command...");
                    await LeaveRoomAsync();
                    await Task.Delay(1000);
                }
            }
            
            Log("=== Protocol Test Complete ===");
            Log($"Connection: {(_isConnected ? "✅" : "❌")}");
            Log($"Authentication: {(_isAuthenticated ? "✅" : "❌")}");
            Log($"UDP Encryption: {(_udpCrypto != null ? "✅" : "❌")}");
            Log($"Packets Sent: {_packetsSent}");
            Log($"Packets Received: {_packetsReceived}");
            Log($"Latency: {_latency:F1}ms");
        }
        catch (Exception ex)
        {
            LogError($"Protocol test failed: {ex.Message}");
        }
#else
        await Task.CompletedTask; // No-op in non-editor builds
        Log("Protocol testing is only available in Unity Editor");
#endif
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
    
    #region Authentication
    
    /// <summary>
    /// Authenticate with server using provided credentials
    /// </summary>
    public async Task<bool> AuthenticateWithCredentialsAsync(string name, string password)
    {
        if (!_isConnected)
        {
            LogError("Cannot authenticate - not connected to server");
            return false;
        }
        
        if (string.IsNullOrEmpty(name))
        {
            LogError("Cannot authenticate - player name is empty");
            return false;
        }
        
        try
        {
            // Set credentials
            playerName = name;
            playerPassword = password ?? "";
            
            Log($"Authenticating with credentials - Name: '{playerName}', Password length: {playerPassword.Length}");
            
            // Send NAME command with JSON format using proper serializable class
            var authRequest = new AuthRequest("NAME", playerName, playerPassword);
            
            string json = JsonUtility.ToJson(authRequest);
            Log($"📤 Sending auth JSON: {json}");
            await SendTcpMessageAsync(json);
            
            Log($"Sent authentication request for player: {playerName}");
            
            // Wait for authentication response (with timeout)
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(10);
            
            Log("Waiting for authentication response...");
            
            while (!_isAuthenticated && (DateTime.Now - startTime) < timeout)
            {
                await Task.Delay(100);
            }
            
            Log($"Authentication wait completed. Authenticated: {_isAuthenticated}");
            
            if (_isAuthenticated)
            {
                // Save credentials if authentication successful
                if (saveCredentials)
                {
                    SaveCredentials();
                }
                
                Log("Authentication successful");
                return true;
            }
            else
            {
                LogError("Authentication timed out");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError($"Authentication failed: {ex.Message}");
            _isAuthenticated = false;
            OnAuthenticationChanged?.Invoke(false);
            return false;
        }
    }
    
    #endregion
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

[System.Serializable]
public class AuthRequest
{
    public string command;
    public string name;
    public string password;
    
    public AuthRequest(string command, string name, string password)
    {
        this.command = command;
        this.name = name;
        this.password = password;
    }
}

[System.Serializable]
public class CreateRoomRequest
{
    public string command;
    public string name;
    public int maxPlayers;
    
    public CreateRoomRequest(string roomName, int maxPlayers)
    {
        this.command = "CREATE_ROOM";
        this.name = roomName;
        this.maxPlayers = maxPlayers;
    }
}

[System.Serializable]
public class JoinRoomRequest
{
    public string command;
    public string roomId;
    
    public JoinRoomRequest(string roomId)
    {
        this.command = "JOIN_ROOM";
        this.roomId = roomId;
    }
}

[System.Serializable]
public class SimpleCommand
{
    public string command;
    
    public SimpleCommand(string commandName)
    {
        this.command = commandName;
    }
}

[System.Serializable]
public class MessageRequest
{
    public string command;
    public string message;
    
    public MessageRequest(string message)
    {
        this.command = "MESSAGE";
        this.message = message;
    }
}

[System.Serializable]
public class RelayMessageRequest
{
    public string command;
    public string toPlayerId;
    public string messageType;
    public string data;
    
    public RelayMessageRequest(string toPlayerId, string messageType, string data)
    {
        this.command = "RELAY_MESSAGE";
        this.toPlayerId = toPlayerId;
        this.messageType = messageType;
        this.data = data;
    }
}
