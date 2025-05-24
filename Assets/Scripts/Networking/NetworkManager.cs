using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Server Configuration")]
    [SerializeField] private string serverHost = "localhost";
    [SerializeField] private int tcpPort = 443;
    [SerializeField] private int udpPort = 443;

    [Header("Network Security")]
    [SerializeField] private bool acceptSelfSignedCertificates = true; // Should be false for production
    [SerializeField] private bool enableUdpEncryption = true;

    [Header("Connection Options")]
    [SerializeField] private float reconnectDelay = 2f;
    [SerializeField] private int maxReconnectAttempts = 3;
    [SerializeField] private float pingInterval = 5f;
    [SerializeField] private float disconnectTimeout = 10f;

    [Header("Rate Limiting")]
    [SerializeField] private float positionUpdateRate = 10f; // Updates per second
    [SerializeField] private float inputUpdateRate = 10f; // Updates per second

    // Connection state
    private TcpClient _tcpClient;
    private SslStream _sslStream;
    private UdpClient _udpClient;
    private NetworkSecurity _networkSecurity;
    private Task _receiveTask;
    private Task _pingTask;
    private CancellationTokenSource _cancellationTokenSource;
    private IPEndPoint _serverEndpoint;

    // Session information
    private string _sessionId;
    private string _playerId;
    private string _playerName;
    private string _playerPassword;
    private string _currentRoomId;
    private bool _isAuthenticated;
    private bool _isConnecting;
    private bool _isConnected;
    private float _lastMessageTime;
    private int _reconnectAttempts = 0;

    // Latency measurement
    private float _currentLatency = 0;
    private float _lastPingSent;

    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action OnConnectionFailed;
    public event Action<Dictionary<string, object>> OnRoomListReceived;
    public event Action<Dictionary<string, object>> OnRoomJoined;
    public event Action<Dictionary<string, object>> OnRoomLeft;
    public event Action<Dictionary<string, object>> OnGameStarted;
    public event Action<Dictionary<string, object>> OnServerMessage;
    public event Action<Dictionary<string, object>> OnRelayedMessage;
    public event Action<string, Vector3, Quaternion> OnPlayerPositionUpdated;
    public event Action<string, Dictionary<string, object>> OnPlayerInputUpdated;

    private void Awake()
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

    private void Start()
    {
        // Initialize server endpoint
        _serverEndpoint = new IPEndPoint(Dns.GetHostAddresses(serverHost)[0], udpPort);
    }

    private void OnDestroy()
    {
        DisconnectFromServer();
    }

    private void Update()
    {
        // Check for disconnection timeout
        if (_isConnected && Time.realtimeSinceStartup - _lastMessageTime > disconnectTimeout)
        {
            Debug.LogWarning("Connection timeout detected. Disconnecting.");
            DisconnectFromServer();
        }
    }

    #region Public Connection API

    /// <summary>
    /// Connect to the server with the specified credentials
    /// </summary>
    public async void ConnectToServer(string playerName, string password)
    {
        if (_isConnected || _isConnecting)
        {
            Debug.Log("Already connected or connecting to server");
            return;
        }

        _playerName = playerName;
        _playerPassword = password;
        _isConnecting = true;

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // Connect TCP client with TLS
            _tcpClient = new TcpClient();
            Debug.Log($"Connecting to {serverHost}:{tcpPort}");
            await _tcpClient.ConnectAsync(serverHost, tcpPort);

            // Setup TLS connection
            _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
            await _sslStream.AuthenticateAsClientAsync(serverHost);
            Debug.Log("TLS authentication successful");

            // Setup UDP client
            _udpClient = new UdpClient();
            _udpClient.Client.ReceiveTimeout = 5000;

            // Read welcome message
            var welcomeBuffer = new byte[1024];
            int bytesRead = await _sslStream.ReadAsync(welcomeBuffer, 0, welcomeBuffer.Length);
            string welcomeMessage = Encoding.UTF8.GetString(welcomeBuffer, 0, bytesRead);
            Debug.Log($"Server welcome: {welcomeMessage}");

            // Extract session ID
            _sessionId = welcomeMessage.Trim().Split('|')[1];
            Debug.Log($"Session ID: {_sessionId}");

            // Configure security
            if (enableUdpEncryption)
            {
                _networkSecurity = new NetworkSecurity(_sessionId, _playerPassword);
            }
            else
            {
                _networkSecurity = new NetworkSecurity();
            }

            _isConnected = true;
            _isConnecting = false;
            _lastMessageTime = Time.realtimeSinceStartup;

            // Start receiving messages
            _receiveTask = ReceiveTcpMessagesAsync(_cancellationTokenSource.Token);
            _pingTask = PingServerPeriodicallyAsync(_cancellationTokenSource.Token);

            // Start receiving UDP
            StartCoroutine(ReceiveUdpMessages());

            // Send NAME command to authenticate
            await SendCommandAsync("NAME", new { name = _playerName, password = _playerPassword });
            
            OnConnected?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Connection failed: {ex}");
            _isConnecting = false;
            DisconnectFromServer();
            OnConnectionFailed?.Invoke();
        }
    }

    /// <summary>
    /// Disconnect from the server
    /// </summary>
    public void DisconnectFromServer()
    {
        if (!_isConnected && !_isConnecting)
            return;

        Debug.Log("Disconnecting from server");

        // Cancel all async operations
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // Close TCP connection
        try
        {
            if (_sslStream != null)
            {
                // Send BYE command first if possible
                if (_isConnected)
                {
                    try
                    {
                        var byeCommand = JsonConvert.SerializeObject(new { command = "BYE" }) + "\n";
                        var byeBytes = Encoding.UTF8.GetBytes(byeCommand);
                        _sslStream.Write(byeBytes, 0, byeBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to send BYE command: {ex.Message}");
                    }
                }

                _sslStream.Close();
                _sslStream.Dispose();
                _sslStream = null;
            }

            _tcpClient?.Close();
            _tcpClient = null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error during TCP disconnection: {ex.Message}");
        }

        // Close UDP connection
        try
        {
            _udpClient?.Close();
            _udpClient = null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error during UDP disconnection: {ex.Message}");
        }

        // Reset state
        _isConnected = false;
        _isAuthenticated = false;

        OnDisconnected?.Invoke();
    }

    /// <summary>
    /// Attempt to reconnect to the server
    /// </summary>
    public void AttemptReconnect()
    {
        if (_isConnected || _isConnecting)
            return;

        if (_reconnectAttempts >= maxReconnectAttempts)
        {
            Debug.LogWarning("Maximum reconnection attempts reached.");
            return;
        }

        _reconnectAttempts++;
        Debug.Log($"Attempting reconnection {_reconnectAttempts}/{maxReconnectAttempts}");

        ConnectToServer(_playerName, _playerPassword);
    }

    #endregion

    #region TCP Commands

    /// <summary>
    /// Request the list of available game rooms
    /// </summary>
    public async Task ListRooms()
    {
        await SendCommandAsync("LIST_ROOMS");
    }

    /// <summary>
    /// Create a new game room
    /// </summary>
    public async Task CreateRoom(string roomName)
    {
        await SendCommandAsync("CREATE_ROOM", new { name = roomName });
    }

    /// <summary>
    /// Join an existing game room
    /// </summary>
    public async Task JoinRoom(string roomId)
    {
        await SendCommandAsync("JOIN_ROOM", new { roomId });
    }

    /// <summary>
    /// Leave the current game room
    /// </summary>
    public async Task LeaveRoom()
    {
        await SendCommandAsync("LEAVE_ROOM");
    }

    /// <summary>
    /// Start the game (host only)
    /// </summary>
    public async Task StartGame()
    {
        await SendCommandAsync("START_GAME");
    }

    /// <summary>
    /// Request the list of players in the current room
    /// </summary>
    public async Task GetRoomPlayers()
    {
        await SendCommandAsync("GET_ROOM_PLAYERS");
    }

    /// <summary>
    /// Send a message to another player
    /// </summary>
    public async Task RelayMessage(string targetId, string message)
    {
        await SendCommandAsync("RELAY_MESSAGE", new { targetId, message });
    }

    /// <summary>
    /// Request information about the player's current session
    /// </summary>
    public async Task RequestPlayerInfo()
    {
        await SendCommandAsync("PLAYER_INFO");
    }

    /// <summary>
    /// Send a ping to the server to measure latency and keep connection alive
    /// </summary>
    public async Task SendPing()
    {
        _lastPingSent = Time.realtimeSinceStartup;
        await SendCommandAsync("PING");
    }

    /// <summary>
    /// Sends a command to the server via TCP
    /// </summary>
    private async Task SendCommandAsync(string command, object data = null)
    {
        if (!_isConnected || _sslStream == null)
        {
            Debug.LogWarning("Cannot send command - not connected");
            return;
        }

        try
        {
            var jsonObj = new Dictionary<string, object> { { "command", command } };

            // Add additional properties from data object if provided
            if (data != null)
            {
                var dataProps = data.GetType().GetProperties();
                foreach (var prop in dataProps)
                {
                    jsonObj[prop.Name] = prop.GetValue(data);
                }
            }

            string jsonStr = JsonConvert.SerializeObject(jsonObj) + "\n";
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonStr);

            await _sslStream.WriteAsync(messageBytes, 0, messageBytes.Length);
            _lastMessageTime = Time.realtimeSinceStartup;
            Debug.Log($"Sent command: {command}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error sending command {command}: {ex}");
            await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => DisconnectFromServer());
        }
    }

    #endregion

    #region UDP Communication

    /// <summary>
    /// Send a position update via UDP
    /// </summary>
    /// <param name="position">The player's position</param>
    /// <param name="rotation">The player's rotation</param>
    public void SendPositionUpdate(Vector3 position, Quaternion rotation)
    {
        if (!_isConnected || _udpClient == null || string.IsNullOrEmpty(_sessionId))
            return;

        try
        {
            var posUpdateData = new
            {
                command = "UPDATE",
                sessionId = _sessionId,
                position = new { x = position.x, y = position.y, z = position.z },
                rotation = new { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
            };

            // Use the security class to create the packet (encrypted if enabled)
            byte[] packetData = _networkSecurity.CreatePacket(posUpdateData);
            _udpClient.Send(packetData, packetData.Length, _serverEndpoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error sending position update: {ex.Message}");
        }
    }

    /// <summary>
    /// Send an input update via UDP
    /// </summary>
    /// <param name="steering">Steering input (-1 to 1)</param>
    /// <param name="throttle">Throttle input (0 to 1)</param>
    /// <param name="brake">Brake input (0 to 1)</param>
    public void SendInputUpdate(float steering, float throttle, float brake)
    {
        if (!_isConnected || _udpClient == null || string.IsNullOrEmpty(_sessionId) || string.IsNullOrEmpty(_currentRoomId))
            return;

        try
        {
            var inputUpdateData = new
            {
                command = "INPUT",
                sessionId = _sessionId,
                roomId = _currentRoomId,
                input = new
                {
                    steering = steering,
                    throttle = throttle,
                    brake = brake,
                    timestamp = Time.realtimeSinceStartup
                },
                client_id = _sessionId
            };

            // Use the security class to create the packet (encrypted if enabled)
            byte[] packetData = _networkSecurity.CreatePacket(inputUpdateData);
            _udpClient.Send(packetData, packetData.Length, _serverEndpoint);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error sending input update: {ex.Message}");
        }
    }

    /// <summary>
    /// Continuously receive UDP messages
    /// </summary>
    private IEnumerator ReceiveUdpMessages()
    {
        if (_udpClient == null)
            yield break;

        // Set up an endpoint to receive from any address
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);

        while (_isConnected && _udpClient != null)
        {
            try
            {
                // Use a task to asynchronously receive UDP data without blocking
                var receiveTask = _udpClient.ReceiveAsync();
                while (!receiveTask.IsCompleted)
                {
                    // Yield control back to Unity until task completes
                    yield return null;
                    
                    // Break out if we've disconnected
                    if (!_isConnected || _udpClient == null)
                        yield break;
                }

                if (receiveTask.IsFaulted)
                {
                    Debug.LogError($"UDP receive error: {receiveTask.Exception}");
                    continue;
                }

                var result = receiveTask.Result;
                var packetData = result.Buffer;

                // Try to parse the received data
                try
                {
                    string json = null;
                    Dictionary<string, object> updateData = null;

                    // Try to decode as encrypted first
                    if (_networkSecurity.IsEncryptionEnabled)
                    {
                        updateData = _networkSecurity.ParsePacket<Dictionary<string, object>>(packetData);
                        json = JsonConvert.SerializeObject(updateData);
                    }

                    // If that fails, try as plain text
                    if (updateData == null)
                    {
                        json = Encoding.UTF8.GetString(packetData).TrimEnd('\n');
                        updateData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    }
                    
                    if (updateData == null)
                    {
                        Debug.LogWarning("Received invalid UDP packet");
                        continue;
                    }

                    // Process the update based on command type
                    if (updateData.TryGetValue("command", out var cmdObj) && cmdObj is string command)
                    {
                        if (command == "UPDATE" && updateData.TryGetValue("sessionId", out var sidObj) && sidObj is string senderId)
                        {
                            // Skip our own updates
                            if (senderId == _sessionId)
                                continue;

                            // Process position update
                            Vector3 position = Vector3.zero;
                            Quaternion rotation = Quaternion.identity;

                            if (updateData.TryGetValue("position", out var posObj) && posObj is Newtonsoft.Json.Linq.JObject posJObj)
                            {
                                position.x = posJObj.Value<float>("x");
                                position.y = posJObj.Value<float>("y");
                                position.z = posJObj.Value<float>("z");
                            }

                            if (updateData.TryGetValue("rotation", out var rotObj) && rotObj is Newtonsoft.Json.Linq.JObject rotJObj)
                            {
                                rotation.x = rotJObj.Value<float>("x");
                                rotation.y = rotJObj.Value<float>("y");
                                rotation.z = rotJObj.Value<float>("z");
                                rotation.w = rotJObj.Value<float>("w");
                            }

                            // Dispatch event on main thread
                            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            {
                                OnPlayerPositionUpdated?.Invoke(senderId, position, rotation);
                            });
                        }
                        else if (command == "INPUT" && updateData.TryGetValue("sessionId", out var sidObj) && sidObj is string senderId)
                        {
                            // Skip our own inputs
                            if (senderId == _sessionId)
                                continue;

                            // Process input update
                            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            {
                                OnPlayerInputUpdated?.Invoke(senderId, updateData);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error processing UDP packet: {ex.Message}");
                }
            }
            catch (ObjectDisposedException)
            {
                // UDP client was disposed, exit the loop
                break;
            }
            catch (Exception ex)
            {
                Debug.LogError($"UDP receive error: {ex.Message}");
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    #endregion

    #region TLS Certificate Validation

    /// <summary>
    /// Validates server certificates according to configuration
    /// </summary>
    private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // In production, this should be more restrictive based on certificate pinning or proper CA validation
        if (acceptSelfSignedCertificates && sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
        {
            Debug.Log("Accepting self-signed certificate");
            return true;
        }

        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        Debug.LogError($"Certificate error: {sslPolicyErrors}");
        return false;
    }

    #endregion

    #region Background Tasks

    /// <summary>
    /// Continuously receive messages from the server
    /// </summary>
    private async Task ReceiveTcpMessagesAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _tcpClient != null && _tcpClient.Connected)
            {
                int bytesRead = await _sslStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    // Connection closed by server
                    Debug.LogWarning("Server closed the connection");
                    break;
                }

                // Add received bytes to message builder
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                _lastMessageTime = Time.realtimeSinceStartup;

                // Process complete messages (lines)
                string data = messageBuilder.ToString();
                int lineBreakIndex;
                while ((lineBreakIndex = data.IndexOf('\n')) >= 0)
                {
                    string line = data.Substring(0, lineBreakIndex).Trim();
                    data = data.Substring(lineBreakIndex + 1);

                    if (!string.IsNullOrEmpty(line))
                    {
                        // Process the complete message
                        await ProcessTcpMessageAsync(line);
                    }
                }

                // Keep any remaining data for the next iteration
                messageBuilder.Clear();
                messageBuilder.Append(data);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, do nothing
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error receiving messages: {ex}");
        }
        finally
        {
            // Clean up when receive loop ends
            await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => DisconnectFromServer());
        }
    }

    /// <summary>
    /// Process a complete message received from TCP
    /// </summary>
    private async Task ProcessTcpMessageAsync(string message)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
            if (response == null || !response.TryGetValue("command", out var cmdObj) || !(cmdObj is string command))
            {
                Debug.LogWarning($"Received invalid message format: {message}");
                return;
            }

            Debug.Log($"Received command: {command}");

            switch (command)
            {
                case "NAME_OK":
                    _isAuthenticated = true;
                    bool hasUdpEncryption = false;
                    if (response.TryGetValue("udpEncryption", out var encryptionObj) && encryptionObj is bool encEnabled)
                    {
                        hasUdpEncryption = encEnabled;
                    }
                    Debug.Log($"Authentication successful. UDP Encryption: {hasUdpEncryption}");
                    break;

                case "AUTH_OK":
                    _isAuthenticated = true;
                    Debug.Log("Authentication successful");
                    break;

                case "PONG":
                    _currentLatency = (Time.realtimeSinceStartup - _lastPingSent) * 1000f;
                    Debug.Log($"Current latency: {_currentLatency:F1}ms");
                    break;

                case "ROOM_CREATED":
                    if (response.TryGetValue("roomId", out var roomIdObj) && roomIdObj is string roomId)
                    {
                        _currentRoomId = roomId;
                        Debug.Log($"Room created with ID: {roomId}");
                    }
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnRoomJoined?.Invoke(response));
                    break;

                case "JOIN_OK":
                    if (response.TryGetValue("roomId", out var joinedRoomIdObj) && joinedRoomIdObj is string joinedRoomId)
                    {
                        _currentRoomId = joinedRoomId;
                        Debug.Log($"Joined room with ID: {joinedRoomId}");
                    }
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnRoomJoined?.Invoke(response));
                    break;

                case "LEAVE_OK":
                    _currentRoomId = null;
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnRoomLeft?.Invoke(response));
                    break;

                case "ROOM_LIST":
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnRoomListReceived?.Invoke(response));
                    break;

                case "GAME_STARTED":
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnGameStarted?.Invoke(response));
                    break;

                case "RELAYED_MESSAGE":
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnRelayedMessage?.Invoke(response));
                    break;

                case "ERROR":
                case "AUTH_FAILED":
                    Debug.LogWarning($"Server error: {message}");
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => OnServerMessage?.Invoke(response));
                    break;

                case "BYE_OK":
                    await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => DisconnectFromServer());
                    break;

                default:
                    Debug.Log($"Unhandled command: {command} - {message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing message '{message}': {ex}");
        }
    }

    /// <summary>
    /// Send periodic pings to measure latency and keep connection alive
    /// </summary>
    private async Task PingServerPeriodicallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected)
            {
                await SendPing();
                await Task.Delay(TimeSpan.FromSeconds(pingInterval), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, do nothing
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in ping task: {ex}");
        }
    }

    #endregion

    /// <summary>
    /// Get the current network latency
    /// </summary>
    public float GetCurrentLatency()
    {
        return _currentLatency;
    }
}