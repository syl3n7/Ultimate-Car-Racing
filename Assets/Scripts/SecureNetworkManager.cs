using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.IO;
using System.Collections;

/// <summary>
/// Secure NetworkManager for connecting to the racing server with TLS encryption
/// Compatible with the new secure server implementation
/// </summary>
public class SecureNetworkManager : MonoBehaviour
{
    public static SecureNetworkManager Instance { get; private set; }
    
    [Header("Connection Settings")]
    public string serverHost = "localhost";
    public int serverPort = 443; // TLS port
    
    [Header("Player Settings")]
    public string playerName = "Player";
    public string playerPassword = "secret123";
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    // Networking components
    private TcpClient _tcpClient;
    private SslStream _sslStream;
    private UdpClient _udpClient;
    private StreamReader _tcpReader;
    private bool _isConnected = false;
    private bool _isAuthenticated = false;
    
    // Session data
    private string _sessionId;
    private string _currentRoomId;
    private string _currentRoomHostId;  // Add room host ID field
    private UdpEncryption _udpCrypto;
    
    // UDP endpoint
    private IPEndPoint _serverUdpEndpoint;
    
    // Events (same as original NetworkManager for compatibility)
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
    
    // Additional events for secure features
    public System.Action<bool> OnConnectionChanged;
    public System.Action<bool> OnAuthenticationChanged;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("SecureNetworkManager initialized");
        }
        else
        {
            Debug.Log("Duplicate SecureNetworkManager destroyed");
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        _serverUdpEndpoint = new IPEndPoint(IPAddress.Parse(serverHost), serverPort);
        LoadSavedCredentials();
    }
    
    #region Connection Management
    
    public async Task<bool> ConnectToServer()
    {
        try
        {
            Log("Connecting to server...");
            
            // Create TCP connection
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(serverHost, serverPort);
            
            // Setup TLS encryption
            _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
            await _sslStream.AuthenticateAsClientAsync(serverHost);
            
            // Setup reader for responses
            _tcpReader = new StreamReader(_sslStream, Encoding.UTF8);
            
            // Read welcome message
            string welcome = await _tcpReader.ReadLineAsync();
            Log($"Server welcome: {welcome}");
            
            if (welcome != null && welcome.StartsWith("CONNECTED|"))
            {
                _sessionId = welcome.Split('|')[1];
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                OnConnected?.Invoke("Connected successfully");
                
                // Start listening for TCP messages
                StartCoroutine(ListenForTcpMessages());
                
                // Authenticate player
                await AuthenticatePlayer();
                
                return true;
            }
            
            throw new Exception("Invalid welcome message from server");
        }
        catch (Exception ex)
        {
            LogError($"Failed to connect: {ex.Message}");
            OnConnectionFailed?.Invoke(ex.Message);
            await Disconnect();
            return false;
        }
    }
    
    private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // For development: Accept self-signed certificates
        // For production: Implement proper certificate validation
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;
            
        if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors ||
            sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
        {
            Log($"Accepting self-signed certificate: {sslPolicyErrors}");
            return true; // Accept for development
        }
        
        LogError($"Certificate validation failed: {sslPolicyErrors}");
        return false;
    }
    
    private async Task AuthenticatePlayer()
    {
        try
        {
            var nameCommand = new
            {
                command = "NAME",
                name = playerName,
                password = playerPassword
            };
            
            await SendTcpMessage(nameCommand);
        }
        catch (Exception ex)
        {
            LogError($"Authentication failed: {ex.Message}");
        }
    }
    
    public async Task Disconnect()
    {
        _isConnected = false;
        _isAuthenticated = false;
        
        try
        {
            if (_sslStream != null)
            {
                await SendTcpMessage(new { command = "BYE" });
                _sslStream.Close();
            }
            
            _tcpClient?.Close();
            _udpClient?.Close();
        }
        catch (Exception ex)
        {
            LogError($"Error during disconnect: {ex.Message}");
        }
        
        OnConnectionChanged?.Invoke(false);
        OnAuthenticationChanged?.Invoke(false);
        OnDisconnected?.Invoke("Disconnected from server");
    }
    
    #endregion
    
    #region TCP Communication
    
    // Public method for external access
    public async Task SendTcpMessage(object message)
    {
        await SendTcpMessage<object>(message);
    }
    
    private async Task SendTcpMessage<T>(T message)
    {
        if (!_isConnected || _sslStream == null)
        {
            LogError("Not connected to server");
            return;
        }
        
        try
        {
            string json = JsonConvert.SerializeObject(message) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _sslStream.WriteAsync(data, 0, data.Length);
            
            Log($"Sent: {json.Trim()}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to send TCP message: {ex.Message}");
        }
    }
    
    private IEnumerator ListenForTcpMessages()
    {
        while (_isConnected && _tcpReader != null)
        {
            Task<string> readTask = _tcpReader.ReadLineAsync();
            
            // Wait for the task to complete
            while (!readTask.IsCompleted)
            {
                yield return null;
            }
            
            if (readTask.Result != null)
            {
                ProcessTcpMessage(readTask.Result);
            }
            else
            {
                // Connection lost
                Log("TCP connection lost");
                break;
            }
        }
        
        // Connection ended
        _ = Disconnect();
    }
    
    private void ProcessTcpMessage(string message)
    {
        try
        {
            Log($"Received: {message}");
            
            var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
            if (root == null || !root.ContainsKey("command"))
                return;
            
            string command = root["command"].ToString();
            
            switch (command)
            {
                case "NAME_OK":
                    HandleNameOk(root);
                    break;
                case "AUTH_FAILED":
                    HandleAuthFailed(root);
                    break;
                case "ROOM_CREATED":
                    HandleRoomCreated(root);
                    break;
                case "JOIN_OK":
                    HandleRoomJoined(root);
                    break;
                case "ROOM_LIST":
                    HandleRoomList(root);
                    break;
                case "GAME_STARTED":
                    HandleGameStarted(root);
                    break;
                case "RELAYED_MESSAGE":
                    HandleRelayedMessage(root);
                    break;
                case "ERROR":
                    HandleError(root);
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
    
    private void HandleNameOk(Dictionary<string, object> root)
    {
        if (root.ContainsKey("authenticated") && Convert.ToBoolean(root["authenticated"]))
        {
            _isAuthenticated = true;
            OnAuthenticationChanged?.Invoke(true);
            
            // Setup UDP encryption if available
            if (root.ContainsKey("udpEncryption") && Convert.ToBoolean(root["udpEncryption"]))
            {
                _udpCrypto = new UdpEncryption(_sessionId);
                SetupUdpClient();
            }
            
            Log("Successfully authenticated with UDP encryption enabled");
        }
    }
    
    private void HandleAuthFailed(Dictionary<string, object> root)
    {
        if (root.ContainsKey("message"))
        {
            LogError($"Authentication failed: {root["message"]}");
        }
        _isAuthenticated = false;
        OnAuthenticationChanged?.Invoke(false);
    }
    
    private void HandleRoomCreated(Dictionary<string, object> root)
    {
        if (root.ContainsKey("roomId") && root.ContainsKey("name"))
        {
            _currentRoomId = root["roomId"].ToString();
            _currentRoomHostId = _sessionId; // Creator becomes the host
            
            var roomData = new Dictionary<string, object>
            {
                { "room_id", _currentRoomId },
                { "room_name", root["name"] }
            };
            
            OnGameHosted?.Invoke(roomData);
        }
    }
    
    private void HandleRoomJoined(Dictionary<string, object> root)
    {
        if (root.ContainsKey("roomId"))
        {
            _currentRoomId = root["roomId"].ToString();
            
            // Set room host ID if provided by server
            if (root.ContainsKey("hostId"))
            {
                _currentRoomHostId = root["hostId"].ToString();
            }
            
            var joinData = new Dictionary<string, object>
            {
                { "room_id", _currentRoomId }
            };
            
            OnRoomJoined?.Invoke(joinData);
        }
    }
    
    private void HandleRoomList(Dictionary<string, object> root)
    {
        if (root.ContainsKey("rooms"))
        {
            OnRoomListReceived?.Invoke(root);
        }
    }
    
    private void HandleGameStarted(Dictionary<string, object> root)
    {
        var gameData = new Dictionary<string, object>();
        
        if (root.ContainsKey("roomId"))
            gameData["room_id"] = root["roomId"];
            
        if (root.ContainsKey("spawnPositions"))
            gameData["spawn_positions"] = root["spawnPositions"];
        
        OnGameStarted?.Invoke(gameData);
    }
    
    private void HandleRelayedMessage(Dictionary<string, object> root)
    {
        var relayData = new Dictionary<string, object>();
        
        if (root.ContainsKey("senderId"))
            relayData["sender_id"] = root["senderId"];
            
        if (root.ContainsKey("message"))
            relayData["message"] = root["message"];
        
        OnRelayReceived?.Invoke(relayData);
    }
    
    private void HandleError(Dictionary<string, object> root)
    {
        if (root.ContainsKey("message"))
        {
            LogError($"Server error: {root["message"]}");
        }
    }
    
    #endregion
    
    #region UDP Communication
    
    private void SetupUdpClient()
    {
        try
        {
            _udpClient = new UdpClient();
            StartCoroutine(ListenForUdpMessages());
            Log("UDP client setup complete");
        }
        catch (Exception ex)
        {
            LogError($"Failed to setup UDP client: {ex.Message}");
        }
    }
    
    public async Task SendPositionUpdate(Vector3 position, Quaternion rotation)
    {
        if (!_isAuthenticated || _udpClient == null || string.IsNullOrEmpty(_currentRoomId))
            return;
        
        try
        {
            var update = new
            {
                command = "UPDATE",
                sessionId = _sessionId,
                position = new { x = position.x, y = position.y, z = position.z },
                rotation = new { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
            };
            
            byte[] data;
            
            if (_udpCrypto != null)
            {
                // Send encrypted packet
                data = _udpCrypto.CreatePacket(update);
            }
            else
            {
                // Fallback to plain text
                string json = JsonConvert.SerializeObject(update);
                data = Encoding.UTF8.GetBytes(json);
            }
            
            await _udpClient.SendAsync(data, data.Length, _serverUdpEndpoint);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send position update: {ex.Message}");
        }
    }
    
    public async Task SendInputUpdate(float steering, float throttle, float brake)
    {
        if (!_isAuthenticated || _udpClient == null || string.IsNullOrEmpty(_currentRoomId))
            return;
        
        try
        {
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
                    timestamp = Time.time
                },
                client_id = _sessionId
            };
            
            byte[] data;
            
            if (_udpCrypto != null)
            {
                data = _udpCrypto.CreatePacket(input);
            }
            else
            {
                string json = JsonConvert.SerializeObject(input);
                data = Encoding.UTF8.GetBytes(json);
            }
            
            await _udpClient.SendAsync(data, data.Length, _serverUdpEndpoint);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send input update: {ex.Message}");
        }
    }
    
    private IEnumerator ListenForUdpMessages()
    {
        while (_isConnected && _udpClient != null)
        {
            Task<UdpReceiveResult> receiveTask = _udpClient.ReceiveAsync();
            
            while (!receiveTask.IsCompleted)
            {
                yield return null;
            }
            
            if (receiveTask.IsCompletedSuccessfully)
            {
                ProcessUdpMessage(receiveTask.Result.Buffer);
            }
        }
    }
    
    private void ProcessUdpMessage(byte[] data)
    {
        try
        {
            Dictionary<string, object> update;
            
            // Try to decrypt if possible
            if (_udpCrypto != null && data.Length >= 4)
            {
                var parsedData = _udpCrypto.ParsePacket<Dictionary<string, object>>(data);
                if (parsedData != null)
                {
                    update = parsedData;
                }
                else
                {
                    // Fallback to plain text
                    string json = Encoding.UTF8.GetString(data);
                    update = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                }
            }
            else
            {
                // Plain text packet
                string json = Encoding.UTF8.GetString(data);
                update = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            
            if (update == null || !update.ContainsKey("command"))
                return;
            
            string command = update["command"].ToString();
            
            if (command == "UPDATE")
            {
                HandlePositionUpdate(update);
            }
            else if (command == "INPUT")
            {
                HandleInputUpdate(update);
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to process UDP message: {ex.Message}");
        }
    }
    
    private void HandlePositionUpdate(Dictionary<string, object> update)
    {
        // Handle position updates from other players
        if (update.ContainsKey("sessionId") && update.ContainsKey("position") && update.ContainsKey("rotation"))
        {
            string playerId = update["sessionId"].ToString();
            
            // Skip our own updates
            if (playerId == _sessionId)
                return;
            
            // Extract position and rotation data
            var posData = update["position"] as Newtonsoft.Json.Linq.JObject;
            var rotData = update["rotation"] as Newtonsoft.Json.Linq.JObject;
            
            if (posData != null && rotData != null)
            {
                Vector3 position = new Vector3(
                    Convert.ToSingle(posData["x"]),
                    Convert.ToSingle(posData["y"]),
                    Convert.ToSingle(posData["z"])
                );
                
                Quaternion rotation = new Quaternion(
                    Convert.ToSingle(rotData["x"]),
                    Convert.ToSingle(rotData["y"]),
                    Convert.ToSingle(rotData["z"]),
                    Convert.ToSingle(rotData["w"])
                );
                
                // Apply to GameManager
                if (GameManager.Instance != null)
                {
                    var playerState = new GameManager.PlayerStateData
                    {
                        playerId = playerId,
                        position = position,
                        rotation = rotation,
                        velocity = Vector3.zero,
                        angularVelocity = Vector3.zero,
                        timestamp = Time.time
                    };
                    
                    GameManager.Instance.ApplyPlayerState(playerState, false);
                }
            }
        }
    }
    
    private void HandleInputUpdate(Dictionary<string, object> update)
    {
        // Handle input updates from other players
        // Implementation depends on your game's input handling system
    }
    
    #endregion
    
    #region Room Management
    
    public async Task CreateRoom(string roomName)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "CREATE_ROOM", name = roomName });
    }
    
    public async Task JoinRoom(string roomId)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "JOIN_ROOM", roomId = roomId });
    }
    
    public async Task StartGame()
    {
        if (!_isAuthenticated || string.IsNullOrEmpty(_currentRoomId)) return;
        
        await SendTcpMessage(new { command = "START_GAME" });
    }
    
    public async Task SendMessage(string targetId, string message)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "RELAY_MESSAGE", targetId = targetId, message = message });
    }
    
    public async Task RequestRoomList()
    {
        if (!_isConnected) return;
        
        await SendTcpMessage(new { command = "LIST_ROOMS" });
    }
    
    #endregion
    
    #region Compatibility Methods (for existing code)
    
    // These methods maintain compatibility with the original NetworkManager interface
    
    public async Task Connect()
    {
        await ConnectToServer();
    }
    
    public void HostGame(string roomName, int maxPlayers = 20)
    {
        _ = CreateRoom(roomName);
    }
    
    public void JoinGame(string roomId)
    {
        _ = JoinRoom(roomId);
    }
    
    public void SendPlayerState(GameManager.PlayerStateData stateData)
    {
        _ = SendPositionUpdate(stateData.position, stateData.rotation);
    }
    
    public void SendPlayerInput(GameManager.PlayerInputData input)
    {
        _ = SendInputUpdate(input.steering, input.throttle, input.brake);
    }
    
    public string GetClientId() => _sessionId;
    public string GetCurrentRoomId() => _currentRoomId;
    public string GetRoomHostId() => _currentRoomHostId;
    public bool IsConnected() => _isConnected;
    public bool IsAuthenticated() => _isAuthenticated;
    
    #endregion
    
    #region Credential Management
    
    public void SetCredentials(string name, string password)
    {
        playerName = name;
        playerPassword = password;
        SaveCredentials();
    }
    
    private void SaveCredentials()
    {
        try
        {
            PlayerPrefs.SetString("SecurePlayerName", playerName);
            PlayerPrefs.SetString("SecurePlayerPassword", playerPassword);
            PlayerPrefs.Save();
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
            if (PlayerPrefs.HasKey("SecurePlayerName"))
            {
                playerName = PlayerPrefs.GetString("SecurePlayerName");
            }
            
            if (PlayerPrefs.HasKey("SecurePlayerPassword"))
            {
                playerPassword = PlayerPrefs.GetString("SecurePlayerPassword");
            }
            
            Log($"Loaded credentials for: {playerName}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to load credentials: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Logging
    
    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[SecureNetwork] {message}");
        }
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[SecureNetwork] {message}");
    }
    
    #endregion
    
    void OnDestroy()
    {
        _ = Disconnect();
    }
}

// Data structures for compatibility
[System.Serializable]
public struct PlayerUpdate
{
    public string SessionId;
    public Vector3 Position;
    public Quaternion Rotation;
}
