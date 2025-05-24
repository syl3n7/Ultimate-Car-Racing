using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;
using System.Net.Security;

namespace CarRacing.Networking
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }
        
        [Header("Server Configuration")]
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private int tcpPort = 443;
        [SerializeField] private int udpPort = 8443;
        [SerializeField] private bool useTLS = true;
        
        [Header("Authentication")]
        [SerializeField] private string playerName = "";
        [SerializeField] private string playerPassword = "";
        [SerializeField] private bool storeCredentialsSecurely = true;
        
        // Network components
        private TcpClient tcpClient;
        private UdpClient udpClient;
        private SslStream sslStream;
        private NetworkStream tcpStream;
        private System.IO.StreamReader reader;
        private System.IO.StreamWriter writer;
        
        // Security components
        private NetworkSecurity networkSecurity;
        
        // Connection state
        private bool isConnected = false;
        private bool isAuthenticated = false;
        private bool useUdpEncryption = false;
        private string sessionId = "";
        private string currentRoomId = "";
        
        // Events
        public Action OnConnected;
        public Action OnDisconnected;
        public Action OnConnectionFailed;
        public Action<Dictionary<string, object>> OnRoomListReceived;
        public Action<Dictionary<string, object>> OnRoomJoined;
        public Action<Dictionary<string, object>> OnGameStarted;
        public Action<Dictionary<string, object>> OnPlayerPositionUpdated;
        public Action<Dictionary<string, object>> OnServerMessage;
        
        // Latency measurement
        private float lastPingSentTime;
        private float currentLatency;
        public float CurrentLatency => currentLatency;
        private float pingInterval = 5f;
        private float nextPingTime = 0f;
        
        // Background processing
        private bool isProcessingMessages = false;
        private Queue<string> messageQueue = new Queue<string>();
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                networkSecurity = new NetworkSecurity();
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        private void Start()
        {
            // Load stored credentials if available
            LoadCredentials();
        }
        
        private void Update()
        {
            // Send periodic ping for latency measurement
            if (isConnected && Time.time > nextPingTime)
            {
                SendPing();
                nextPingTime = Time.time + pingInterval;
            }
            
            // Process queued messages on main thread
            ProcessMessageQueue();
        }
        
        private void OnDestroy()
        {
            Disconnect();
        }
        
        private void OnApplicationQuit()
        {
            Disconnect();
        }
        
        /// <summary>
        /// Connect to the game server with optional TLS encryption
        /// </summary>
        public async void Connect()
        {
            if (isConnected)
            {
                Debug.Log("Already connected to server");
                return;
            }
            
            try
            {
                Debug.Log($"Connecting to server at {serverAddress}:{tcpPort}");
                
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(serverAddress, tcpPort);
                
                if (useTLS)
                {
                    // Set up TLS with proper certificate validation
                    sslStream = new SslStream(
                        tcpClient.GetStream(), 
                        false,
                        new RemoteCertificateValidationCallback(ValidateServerCertificate),
                        null
                    );
                    
                    // Authenticate as client (true = use client cert, we don't need this)
                    await sslStream.AuthenticateAsClientAsync(serverAddress);
                    
                    reader = new System.IO.StreamReader(sslStream, Encoding.UTF8);
                    writer = new System.IO.StreamWriter(sslStream, Encoding.UTF8);
                }
                else
                {
                    // Fallback to regular TCP (non-encrypted)
                    Debug.LogWarning("Using unencrypted TCP connection - not recommended for production!");
                    tcpStream = tcpClient.GetStream();
                    reader = new System.IO.StreamReader(tcpStream, Encoding.UTF8);
                    writer = new System.IO.StreamWriter(tcpStream, Encoding.UTF8);
                }
                
                // Set up UDP client for game state updates
                udpClient = new UdpClient();
                
                // Start receiving TCP messages
                isConnected = true;
                StartMessageReceiver();
                
                // Handle welcome message to get session ID
                string welcomeMessage = await reader.ReadLineAsync();
                Debug.Log($"Received welcome message: {welcomeMessage}");
                
                // Extract session ID from welcome message format "CONNECTED|<sessionId>"
                if (welcomeMessage.StartsWith("CONNECTED|"))
                {
                    sessionId = welcomeMessage.Split('|')[1];
                    Debug.Log($"Session ID: {sessionId}");
                    
                    // Trigger connected event
                    OnConnected?.Invoke();
                    
                    // Send authentication if we have credentials
                    if (!string.IsNullOrEmpty(playerName))
                    {
                        SendNameCommand(playerName, playerPassword);
                    }
                }
                else
                {
                    Debug.LogError("Invalid welcome message format");
                    Disconnect();
                    OnConnectionFailed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection failed: {ex.Message}");
                Disconnect();
                OnConnectionFailed?.Invoke();
            }
        }
        
        /// <summary>
        /// Disconnect from the server and clean up resources
        /// </summary>
        public void Disconnect()
        {
            if (!isConnected)
                return;
                
            try
            {
                // Send BYE command if still connected
                if (tcpClient != null && tcpClient.Connected)
                {
                    SendCommand("BYE");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error sending BYE command: {ex.Message}");
            }
            
            try
            {
                isConnected = false;
                isAuthenticated = false;
                useUdpEncryption = false;
                
                // Clean up security resources
                networkSecurity.Cleanup();
                
                // Close TCP connections
                writer?.Close();
                reader?.Close();
                sslStream?.Close();
                tcpStream?.Close();
                tcpClient?.Close();
                
                // Close UDP client
                udpClient?.Close();
                
                // Reset variables
                sessionId = "";
                currentRoomId = "";
                
                // Notify listeners
                OnDisconnected?.Invoke();
                
                Debug.Log("Disconnected from server");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during disconnect: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Send player name for registration or authentication
        /// </summary>
        public void SendNameCommand(string name, string password)
        {
            if (!isConnected)
            {
                Debug.LogError("Cannot send NAME command - not connected");
                return;
            }
            
            playerName = name;
            playerPassword = password;
            
            // Construct NAME command according to protocol
            var command = new Dictionary<string, object>
            {
                { "command", "NAME" },
                { "name", name }
            };
            
            // Add password if provided
            if (!string.IsNullOrEmpty(password))
            {
                command["password"] = password;
                
                // Save credentials securely if enabled
                if (storeCredentialsSecurely)
                {
                    SaveCredentials(name, password);
                }
            }
            
            SendCommand(JsonConvert.SerializeObject(command));
        }
        
        /// <summary>
        /// Request authentication with saved password
        /// </summary>
        public void Authenticate()
        {
            if (!isConnected || string.IsNullOrEmpty(playerPassword))
            {
                Debug.LogError("Cannot authenticate - not connected or no password");
                return;
            }
            
            var command = new Dictionary<string, object>
            {
                { "command", "AUTHENTICATE" },
                { "password", playerPassword }
            };
            
            SendCommand(JsonConvert.SerializeObject(command));
        }
        
        /// <summary>
        /// Request the list of available rooms
        /// </summary>
        public void RequestRoomList()
        {
            SendCommand("{\"command\":\"LIST_ROOMS\"}");
        }
        
        /// <summary>
        /// Create a new game room
        /// </summary>
        public void CreateRoom(string roomName)
        {
            if (!isAuthenticated)
            {
                Debug.LogError("Must be authenticated to create a room");
                return;
            }
            
            var command = new Dictionary<string, object>
            {
                { "command", "CREATE_ROOM" },
                { "name", roomName }
            };
            
            SendCommand(JsonConvert.SerializeObject(command));
        }
        
        /// <summary>
        /// Join an existing room
        /// </summary>
        public void JoinRoom(string roomId)
        {
            if (!isAuthenticated)
            {
                Debug.LogError("Must be authenticated to join a room");
                return;
            }
            
            var command = new Dictionary<string, object>
            {
                { "command", "JOIN_ROOM" },
                { "roomId", roomId }
            };
            
            SendCommand(JsonConvert.SerializeObject(command));
        }
        
        /// <summary>
        /// Start the game (host only)
        /// </summary>
        public void StartGame()
        {
            if (!isAuthenticated)
            {
                Debug.LogError("Must be authenticated to start a game");
                return;
            }
            
            SendCommand("{\"command\":\"START_GAME\"}");
        }
        
        /// <summary>
        /// Leave the current room
        /// </summary>
        public void LeaveRoom()
        {
            if (!isAuthenticated)
            {
                Debug.LogError("Must be authenticated to leave a room");
                return;
            }
            
            SendCommand("{\"command\":\"LEAVE_ROOM\"}");
            currentRoomId = "";
        }
        
        /// <summary>
        /// Request player information
        /// </summary>
        public void RequestPlayerInfo()
        {
            SendCommand("{\"command\":\"PLAYER_INFO\"}");
        }
        
        /// <summary>
        /// Send a position update via UDP
        /// </summary>
        public void SendPositionUpdate(Vector3 position, Quaternion rotation)
        {
            if (!isConnected || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(currentRoomId))
            {
                // Not ready to send position updates
                return;
            }
            
            try
            {
                // Create position update object according to protocol
                var updateData = new Dictionary<string, object>
                {
                    { "command", "UPDATE" },
                    { "sessionId", sessionId },
                    { "position", new Dictionary<string, float>
                        {
                            { "x", position.x },
                            { "y", position.y },
                            { "z", position.z }
                        }
                    },
                    { "rotation", new Dictionary<string, float>
                        {
                            { "x", rotation.x },
                            { "y", rotation.y },
                            { "z", rotation.z },
                            { "w", rotation.w }
                        }
                    }
                };
                
                byte[] data;
                
                // Use encryption if available
                if (useUdpEncryption)
                {
                    data = networkSecurity.CreatePacket(updateData);
                }
                else
                {
                    // Fallback to plain text JSON for unauthenticated users
                    string json = JsonConvert.SerializeObject(updateData) + "\n";
                    data = Encoding.UTF8.GetBytes(json);
                }
                
                if (data != null)
                {
                    // Send UDP packet
                    udpClient.Send(data, data.Length, serverAddress, udpPort);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error sending position update: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Send input controls via UDP
        /// </summary>
        public void SendInputControls(float steering, float throttle, float brake, float timestamp)
        {
            if (!isConnected || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(currentRoomId))
            {
                // Not ready to send input updates
                return;
            }
            
            try
            {
                // Create input update object according to protocol
                var inputData = new Dictionary<string, object>
                {
                    { "command", "INPUT" },
                    { "sessionId", sessionId },
                    { "roomId", currentRoomId },
                    { "input", new Dictionary<string, float>
                        {
                            { "steering", steering },
                            { "throttle", throttle },
                            { "brake", brake },
                            { "timestamp", timestamp }
                        }
                    },
                    { "client_id", sessionId }
                };
                
                byte[] data;
                
                // Use encryption if available
                if (useUdpEncryption)
                {
                    data = networkSecurity.CreatePacket(inputData);
                }
                else
                {
                    // Fallback to plain text JSON for unauthenticated users
                    string json = JsonConvert.SerializeObject(inputData) + "\n";
                    data = Encoding.UTF8.GetBytes(json);
                }
                
                if (data != null)
                {
                    // Send UDP packet
                    udpClient.Send(data, data.Length, serverAddress, udpPort);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error sending input controls: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Send a ping to measure latency
        /// </summary>
        private void SendPing()
        {
            if (!isConnected)
                return;
                
            lastPingSentTime = Time.time;
            SendCommand("{\"command\":\"PING\"}");
        }
        
        /// <summary>
        /// Send a JSON command via TCP
        /// </summary>
        private async void SendCommand(string jsonCommand)
        {
            if (!isConnected)
            {
                Debug.LogError("Cannot send command - not connected");
                return;
            }
            
            try
            {
                await writer.WriteLineAsync(jsonCommand);
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error sending command: {ex.Message}");
                HandleConnectionError();
            }
        }
        
        /// <summary>
        /// Start receiving TCP messages in the background
        /// </summary>
        private async void StartMessageReceiver()
        {
            try
            {
                while (isConnected)
                {
                    string message = await reader.ReadLineAsync();
                    
                    if (message == null)
                    {
                        // Connection closed
                        HandleConnectionError();
                        break;
                    }
                    
                    // Queue message for processing on main thread
                    lock (messageQueue)
                    {
                        messageQueue.Enqueue(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in message receiver: {ex.Message}");
                HandleConnectionError();
            }
        }
        
        /// <summary>
        /// Process messages on the main thread
        /// </summary>
        private void ProcessMessageQueue()
        {
            if (isProcessingMessages)
                return;
                
            isProcessingMessages = true;
            
            try
            {
                string message;
                bool hasMessages = false;
                
                lock (messageQueue)
                {
                    hasMessages = messageQueue.Count > 0;
                    
                    while (messageQueue.Count > 0)
                    {
                        message = messageQueue.Dequeue();
                        ProcessMessage(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing message queue: {ex.Message}");
            }
            finally
            {
                isProcessingMessages = false;
            }
        }
        
        /// <summary>
        /// Process a received message
        /// </summary>
        private void ProcessMessage(string message)
        {
            try
            {
                // Parse JSON message
                var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                
                if (msg == null || !msg.ContainsKey("command"))
                {
                    Debug.LogWarning($"Received invalid message: {message}");
                    return;
                }
                
                string command = msg["command"].ToString();
                
                // Handle common responses
                switch (command)
                {
                    case "PONG":
                        // Calculate latency
                        currentLatency = (Time.time - lastPingSentTime) * 1000f; // Convert to ms
                        break;
                        
                    case "NAME_OK":
                        // Successful authentication or name registration
                        isAuthenticated = msg.ContainsKey("authenticated") && 
                                         (msg["authenticated"].ToString() == "True" || msg["authenticated"].ToString() == "true");
                        
                        // Check if UDP encryption is enabled
                        useUdpEncryption = msg.ContainsKey("udpEncryption") && 
                                          (msg["udpEncryption"].ToString() == "True" || msg["udpEncryption"].ToString() == "true");
                        
                        // Set up UDP encryption if available
                        if (useUdpEncryption)
                        {
                            networkSecurity.SetupEncryption(sessionId);
                        }
                        
                        Debug.Log($"NAME_OK received. Authenticated: {isAuthenticated}, UDP Encryption: {useUdpEncryption}");
                        break;
                        
                    case "AUTH_OK":
                        isAuthenticated = true;
                        Debug.Log("Authentication successful");
                        break;
                        
                    case "AUTH_FAILED":
                        isAuthenticated = false;
                        Debug.LogWarning("Authentication failed");
                        OnServerMessage?.Invoke(msg);
                        break;
                        
                    case "ROOM_CREATED":
                        if (msg.ContainsKey("roomId"))
                        {
                            currentRoomId = msg["roomId"].ToString();
                        }
                        OnRoomJoined?.Invoke(msg);
                        break;
                        
                    case "ROOM_JOINED":
                        if (msg.ContainsKey("roomId"))
                        {
                            currentRoomId = msg["roomId"].ToString();
                        }
                        OnRoomJoined?.Invoke(msg);
                        break;
                        
                    case "ROOM_LIST":
                        OnRoomListReceived?.Invoke(msg);
                        break;
                        
                    case "GAME_STARTED":
                        OnGameStarted?.Invoke(msg);
                        break;
                        
                    case "ERROR":
                    case "UNKNOWN_COMMAND":
                        Debug.LogWarning($"Server error: {message}");
                        OnServerMessage?.Invoke(msg);
                        break;
                        
                    default:
                        Debug.Log($"Received message: {message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing message: {ex.Message}\nMessage: {message}");
            }
        }
        
        /// <summary>
        /// Handle connection errors
        /// </summary>
        private void HandleConnectionError()
        {
            if (!isConnected)
                return;
                
            Debug.LogWarning("Connection error detected");
            Disconnect();
        }
        
        /// <summary>
        /// Save player credentials securely
        /// </summary>
        private void SaveCredentials(string name, string password)
        {
            try
            {
                // Use PlayerPrefs for this example, but in a real game you would use 
                // more secure storage like Unity's Keychain on iOS or a similar secure storage method
                PlayerPrefs.SetString("PlayerName", name);
                
                // WARNING: Do not store passwords in PlayerPrefs in a real game!
                // This is just for demonstration purposes
                // For production, use secure storage or a token-based auth system
                if (storeCredentialsSecurely)
                {
                    // Example only - do NOT use this in production:
                    PlayerPrefs.SetString("PlayerAuth", Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(password)));
                }
                
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving credentials: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load player credentials
        /// </summary>
        private void LoadCredentials()
        {
            try
            {
                if (PlayerPrefs.HasKey("PlayerName"))
                {
                    playerName = PlayerPrefs.GetString("PlayerName");
                    
                    // Only load password if secure storage is enabled
                    if (storeCredentialsSecurely && PlayerPrefs.HasKey("PlayerAuth"))
                    {
                        string encoded = PlayerPrefs.GetString("PlayerAuth");
                        
                        // Example only - do NOT use this in production:
                        playerPassword = Encoding.UTF8.GetString(
                            Convert.FromBase64String(encoded));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading credentials: {ex.Message}");
            }
        }

        // Update certificate validation callback to be more secure
        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // For production, implement proper certificate validation
            if (isTrustAllCertificates)
            {
                // Development mode - accept self-signed certificates, but log a warning
                Debug.LogWarning("SECURITY WARNING: Accepting self-signed certificates (development mode)");
                return true;
            }
            
            // Production mode - properly verify certificates
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
                
            // Log specific validation errors
            Debug.LogError($"Certificate error: {sslPolicyErrors}");
            
            // In production, we shouldn't accept invalid certificates
            return false;
        }
    }
}