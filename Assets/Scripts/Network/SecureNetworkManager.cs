using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication; // Added for SslProtocols
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.IO; // Added for IOException
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
    
    // Room cache to store host information
    private Dictionary<string, string> _roomHostCache = new Dictionary<string, string>();
    
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
        // Resolve server host to IP address
        try
        {
            if (IPAddress.TryParse(serverHost, out IPAddress ipAddress))
            {
                _serverUdpEndpoint = new IPEndPoint(ipAddress, serverPort);
            }
            else
            {
                // Resolve hostname to IP address
                var hostEntry = System.Net.Dns.GetHostEntry(serverHost);
                _serverUdpEndpoint = new IPEndPoint(hostEntry.AddressList[0], serverPort);
            }
            Log($"Server endpoint resolved to: {_serverUdpEndpoint}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to resolve server host '{serverHost}': {ex.Message}");
            // Fallback to localhost
            _serverUdpEndpoint = new IPEndPoint(IPAddress.Loopback, serverPort);
        }
        
        LoadSavedCredentials();
    }
    
    #region Connection Management
    
    public async Task<bool> ConnectToServer()
    {
        try
        {
            Log("Connecting to server...");
            
            // Create TCP connection with keepalive enabled
            _tcpClient = new TcpClient();
            
            // Set advanced socket options to prevent premature connection termination
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            
            // Set reasonable timeouts
            _tcpClient.SendTimeout = 30000;  // 30 seconds
            _tcpClient.ReceiveTimeout = 30000;  // 30 seconds
            
            // Use cancellation token for connection timeout
            using (var timeoutCts = new CancellationTokenSource(10000)) // 10 second timeout
            {
                try {
                    var connectTask = _tcpClient.ConnectAsync(serverHost, serverPort);
                    
                    // Wait for either connection or timeout
                    var completedTask = await Task.WhenAny(connectTask, Task.Delay(10000, timeoutCts.Token));
                    
                    if (completedTask != connectTask) {
                        throw new TimeoutException("Connection timed out");
                    }
                    
                    // Ensure the connection task is complete
                    await connectTask;
                    
                    Log($"TCP connection established to {serverHost}:{serverPort}");
                }
                catch (TaskCanceledException) {
                    throw new TimeoutException("Connection timed out");
                }
            }
            
            // Setup TLS encryption with explicit TLS 1.2 protocol
            _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate, null);
            
            // Create SSL options with TLS 1.2 explicitly enabled
            try {
                // Define TLS protocols to use - explicitly set to TLS 1.2
                SslProtocols protocol = SslProtocols.Tls12;
                
                // Set longer timeouts for the handshake process
                _tcpClient.SendTimeout = 30000;  // 30 seconds
                _tcpClient.ReceiveTimeout = 30000;  // 30 seconds
                
                // Authenticate with explicit protocol version and server name
                await _sslStream.AuthenticateAsClientAsync(
                    serverHost,                      // Target host name
                    null,                            // No client certificates
                    protocol,                        // Use TLS 1.2 explicitly  
                    false                            // Don't check certificate revocation
                );
                
                // Ensure the TLS handshake is completed properly
                await _sslStream.FlushAsync();
                
                // Add a small delay to ensure the handshake completes
                await Task.Delay(100);
                
                Log($"TLS connection established with protocol: {_sslStream.SslProtocol}");
            }
            catch (System.Security.Authentication.AuthenticationException authEx) {
                LogError($"TLS authentication failed: {authEx.Message}");
                throw;
            }
            catch (Exception ex) {
                LogError($"TLS general error: {ex.Message}");
                throw;
            }
            
            // Setup reader for responses with a buffer size that can handle larger messages
            _tcpReader = new StreamReader(_sslStream, Encoding.UTF8, true, 4096);
            
            // Read welcome message with timeout to avoid indefinite waiting
            string welcome = null;
            int retryCount = 0;
            const int maxRetries = 3;
            
            while (welcome == null && retryCount < maxRetries)
            {
                try
                {
                    // Use cancellation token for timeout
                    using (var cts = new CancellationTokenSource(5000)) // 5 second timeout
                    {
                        Log($"Waiting for server welcome message (attempt {retryCount + 1}/{maxRetries})");
                        
                        // ReadLine could hang, so we need a timeout
                        Task<string> readTask = _tcpReader.ReadLineAsync();
                        var welcomeReadTimeout = Task.Delay(5000, cts.Token); // 5 second timeout
                        
                        Task finishedTask = await Task.WhenAny(readTask, welcomeReadTimeout);
                        if (finishedTask != readTask)
                        {
                            if (retryCount < maxRetries - 1)
                            {
                                Log("Timed out waiting for server welcome message, retrying...");
                                retryCount++;
                                await Task.Delay(500); // Wait a bit before retry
                                continue;
                            }
                            else
                            {
                                throw new TimeoutException("Timed out waiting for server welcome message after multiple attempts");
                            }
                        }
                        
                        welcome = await readTask;
                    }
                }
                catch (Exception ex)
                {
                    if (retryCount < maxRetries - 1)
                    {
                        LogError($"Error reading welcome message: {ex.Message}, retrying...");
                        retryCount++;
                        await Task.Delay(500); // Wait a bit before retry
                        continue;
                    }
                    else
                    {
                        throw new Exception($"Failed to read welcome message after {maxRetries} attempts: {ex.Message}");
                    }
                }
            }
            
            // Handle null or empty welcome message
            if (string.IsNullOrEmpty(welcome))
            {
                throw new Exception("Received empty welcome message from server");
            }
            
            Log($"Server welcome: {welcome}");
            
            if (welcome.StartsWith("CONNECTED|"))
            {
                string[] parts = welcome.Split('|');
                if (parts.Length < 2)
                {
                    throw new FormatException("Invalid welcome format: missing session ID");
                }
                
                _sessionId = parts[1];
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                OnConnected?.Invoke("Connected successfully");
                
                // Start listening for TCP messages
                StartCoroutine(ListenForTcpMessages());
                
                // Authenticate player
                await AuthenticatePlayer();
                
                return true;
            }
            
            throw new Exception($"Invalid welcome message from server: {welcome}");
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
        {
            Log("Certificate validation successful");
            return true;
        }
            
        // Log detailed certificate information for debugging
        X509Certificate2 cert2 = new X509Certificate2(certificate);
        Log($"Certificate subject: {cert2.Subject}");
        Log($"Certificate issuer: {cert2.Issuer}");
        Log($"Certificate valid from: {cert2.NotBefore} to {cert2.NotAfter}");
        
        // Check if the errors are only related to self-signed certificates
        // Use bitwise AND to check for flag presence, since multiple errors can be combined
        bool hasChainErrors = (sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0;
        bool hasNameMismatch = (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0;
        bool hasNotAvailable = (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0;
        
        Log($"SSL Error Analysis - Chain: {hasChainErrors}, Name: {hasNameMismatch}, NotAvailable: {hasNotAvailable}");
        
        // Accept certificates with only chain errors or name mismatch (common with self-signed certs)
        // but reject if certificate is not available at all
        if ((hasChainErrors || hasNameMismatch) && !hasNotAvailable)
        {
            Log($"Accepting self-signed certificate despite errors: {sslPolicyErrors}");
            return true; // Accept for development with self-signed certificates
        }
        
        LogError($"Certificate validation failed with errors: {sslPolicyErrors}");
        return false; // Reject certificates that are completely unavailable or have other issues
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
            
            // Add cancellation token to prevent indefinite waiting
            using (var cts = new CancellationTokenSource(5000)) // 5 second timeout
            {
                try {
                    await _sslStream.WriteAsync(data, 0, data.Length, cts.Token);
                    
                    // Ensure the data is sent immediately
                    await _sslStream.FlushAsync(cts.Token);
                    
                    Log($"Sent: {json.Trim()}");
                }
                catch (TaskCanceledException) {
                    LogError("Sending TCP message timed out");
                    
                    // If we get a timeout, check the connection
                    if (_tcpClient != null && !_tcpClient.Connected) {
                        LogError("TCP client disconnected, attempting to reconnect...");
                        _ = Disconnect();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to send TCP message: {ex.Message}");
            
            // Check if the exception indicates a connection failure
            if (ex is IOException || ex is SocketException) {
                LogError("Connection may be broken. Attempting to reconnect...");
                _ = Disconnect();
            }
        }
    }
    
    private IEnumerator ListenForTcpMessages()
    {
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 3;
        
        while (_isConnected && _tcpReader != null)
        {
            string message = null;
            bool hasError = false;
            
            // Start reading the next message
            var readTask = _tcpReader?.ReadLineAsync();
            if (readTask == null)
            {
                LogError("TCP reader is not available");
                break;
            }
            
            // Wait for the read to complete or timeout
            float startTime = Time.time;
            while (!readTask.IsCompleted && !readTask.IsFaulted && !readTask.IsCanceled)
            {
                // Check for timeout
                if (Time.time - startTime > 30f)  // 30 second timeout
                {
                    Log("TCP read timed out");
                    hasError = true;
                    break;
                }
                
                yield return null;
            }
            
            // Handle the completed task
            if (!hasError)
            {
                try
                {
                    if (readTask.IsCompleted && !readTask.IsFaulted && !readTask.IsCanceled)
                    {
                        message = readTask.Result;
                    }
                    else if (readTask.IsFaulted)
                    {
                        LogError($"TCP read task faulted: {readTask.Exception?.Message}");
                        hasError = true;
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Exception getting TCP read result: {ex.Message}");
                    hasError = true;
                }
            }
            
            // Process the message if we got one
            if (!hasError && !string.IsNullOrEmpty(message))
            {
                try
                {
                    ProcessTcpMessage(message);
                    consecutiveErrors = 0;  // Reset error counter on success
                }
                catch (Exception ex)
                {
                    LogError($"Error processing message: {ex.Message}");
                    hasError = true;
                }
            }
            else if (!hasError && string.IsNullOrEmpty(message))
            {
                LogError("Received empty TCP message");
                hasError = true;
            }
            
            // Handle errors by incrementing counter and potentially disconnecting
            if (hasError)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    LogError($"Too many consecutive errors ({maxConsecutiveErrors}), disconnecting");
                    break;
                }
                
                yield return new WaitForSeconds(1f);  // Wait a bit before retrying
            }
        }
        
        // Connection ended
        LogError("TCP message listener ended");
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
        Log("Received NAME_OK response from server");
        
        if (root.ContainsKey("authenticated") && Convert.ToBoolean(root["authenticated"]))
        {
            _isAuthenticated = true;
            OnAuthenticationChanged?.Invoke(true);
            Log("✅ Player authenticated successfully");
            
            // Setup UDP encryption if available
            if (root.ContainsKey("udpEncryption") && Convert.ToBoolean(root["udpEncryption"]))
            {
                Log("Server supports UDP encryption - initializing...");
                try
                {
                    _udpCrypto = new UdpEncryption(_sessionId);
                    SetupUdpClient();
                    Log("✅ UDP encryption initialized successfully");
                }
                catch (Exception ex)
                {
                    LogError($"❌ Failed to initialize UDP encryption: {ex.Message}");
                }
            }
            else
            {
                LogError("⚠️ Server response missing udpEncryption=true - UDP will not be encrypted!");
                SetupUdpClient(); // Setup UDP without encryption as fallback
            }
        }
        else
        {
            LogError("❌ Authentication failed - server did not confirm authentication");
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
            else
            {
                // Try to get host ID from cached room information
                if (_roomHostCache.ContainsKey(_currentRoomId))
                {
                    _currentRoomHostId = _roomHostCache[_currentRoomId];
                    if (enableDebugLogs)
                        Debug.Log($"Using cached host ID for room {_currentRoomId}: {_currentRoomHostId}");
                }
                else
                {
                    Debug.LogWarning($"JOIN_OK response missing hostId and no cached info for room {_currentRoomId}");
                }
            }
            
            var joinData = new Dictionary<string, object>
            {
                { "room_id", _currentRoomId }
            };
            
            // Add host_id if we have it
            if (!string.IsNullOrEmpty(_currentRoomHostId))
            {
                joinData["host_id"] = _currentRoomHostId;
            }
            else
            {
                // Fallback: assume we're not the host if we can't determine
                joinData["host_id"] = "unknown";
                Debug.LogWarning("Unable to determine host_id for joined room, setting to 'unknown'");
            }
            
            OnRoomJoined?.Invoke(joinData);
        }
    }
    
    private void HandleRoomList(Dictionary<string, object> root)
    {
        if (root.ContainsKey("rooms"))
        {
            // Cache room host information for later use
            try
            {
                var roomsData = root["rooms"];
                if (roomsData is Newtonsoft.Json.Linq.JArray roomsArray)
                {
                    foreach (var room in roomsArray)
                    {
                        if (room is Newtonsoft.Json.Linq.JObject roomObj)
                        {
                            var roomId = roomObj["id"]?.ToString();
                            var hostId = roomObj["hostId"]?.ToString();
                            
                            if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(hostId))
                            {
                                _roomHostCache[roomId] = hostId;
                                if (enableDebugLogs)
                                    Debug.Log($"Cached host info: Room {roomId} -> Host {hostId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to cache room host information: {ex.Message}");
            }
            
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
        {
            if (enableDebugLogs)
                Log($"Cannot send position update - Auth: {_isAuthenticated}, UDP: {_udpClient != null}, Room: {!string.IsNullOrEmpty(_currentRoomId)}");
            return;
        }

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
                if (enableDebugLogs)
                    Log($"🔒 Sending encrypted position update ({data.Length} bytes)");
            }
            else
            {
                // Fallback to plain text
                string json = JsonConvert.SerializeObject(update);
                data = Encoding.UTF8.GetBytes(json);
                if (enableDebugLogs)
                    LogError($"🔓 Sending UNENCRYPTED position update ({data.Length} bytes) - Security Risk!");
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
        {
            if (enableDebugLogs)
                Log($"Cannot send input update - Auth: {_isAuthenticated}, UDP: {_udpClient != null}, Room: {!string.IsNullOrEmpty(_currentRoomId)}");
            return;
        }

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
                if (enableDebugLogs)
                    Log($"🔒 Sending encrypted input update ({data.Length} bytes)");
            }
            else
            {
                string json = JsonConvert.SerializeObject(input);
                data = Encoding.UTF8.GetBytes(json);
                if (enableDebugLogs)
                    LogError($"🔓 Sending UNENCRYPTED input update ({data.Length} bytes) - Security Risk!");
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
                    if (enableDebugLogs)
                        Log($"🔒 Received encrypted UDP message ({data.Length} bytes)");
                }
                else
                {
                    // Fallback to plain text
                    string json = Encoding.UTF8.GetString(data);
                    update = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (enableDebugLogs)
                        LogError($"🔓 Received UNENCRYPTED UDP message ({data.Length} bytes) - Security Risk!");
                }
            }
            else
            {
                // Plain text packet
                string json = Encoding.UTF8.GetString(data);
                update = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (_udpCrypto != null && enableDebugLogs)
                    LogError($"🔓 Received plain text UDP when encryption available - packet too small or malformed");
            }

            if (update == null || !update.ContainsKey("command"))
            {
                LogError("Received malformed UDP message - missing command field");
                return;
            }

            string command = update["command"].ToString();

            if (command == "UPDATE")
            {
                HandlePositionUpdate(update);
            }
            else if (command == "INPUT")
            {
                HandleInputUpdate(update);
            }
            else
            {
                if (enableDebugLogs)
                    Log($"Received unknown UDP command: {command}");
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
    
    public async Task RequestPlayerInfo()
    {
        if (!_isConnected) return;
        
        await SendTcpMessage(new { command = "PLAYER_INFO" });
    }
    
    public async Task GetRoomPlayers(string roomId)
    {
        if (!_isConnected || string.IsNullOrEmpty(roomId)) return;
        
        await SendTcpMessage(new { command = "GET_ROOM_PLAYERS", roomId = roomId });
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
    
    public async Task LeaveGame()
    {
        if (!_isConnected || string.IsNullOrEmpty(_currentRoomId)) return;
        
        await SendTcpMessage(new { command = "LEAVE_ROOM", roomId = _currentRoomId });
        _currentRoomId = null;
        _currentRoomHostId = null;
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
    
    // Add latency measurement method
    private float _latency = 0f;
    public float GetLatency() => _latency;
    
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
