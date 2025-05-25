using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }
    
    [Header("Connection Settings")]
    [SerializeField] private string serverAddress = "192.168.3.41";
    [SerializeField] private int serverPort = 8000; 
    [SerializeField] private bool useTls = false; // Set to false to disable TLS
    
    [Header("Debug Options")]
    [SerializeField] private bool showDebugLogs = true;
    
    private TcpClient tcpClient;
    private SslStream sslStream;
    private NetworkStream networkStream;
    private Thread receiveThread;
    private CancellationTokenSource cancellationTokenSource;
    private Queue<Dictionary<string, object>> messageQueue = new Queue<Dictionary<string, object>>();
    private object lockObject = new object();
    
    // Connection status
    private bool isConnected = false;
    private string sessionId = "";
    private string currentRoomId = "";
    
    // Events
    public delegate void MessageReceivedHandler(Dictionary<string, object> message);
    public event MessageReceivedHandler OnMessageReceived;
    public event MessageReceivedHandler OnRoomListReceived;
    public event MessageReceivedHandler OnRoomJoined;
    public event MessageReceivedHandler OnRoomCreated;
    public event MessageReceivedHandler OnPlayerJoined;
    public event MessageReceivedHandler OnGameStarted;
    public event MessageReceivedHandler OnPlayerLeft;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            cancellationTokenSource = new CancellationTokenSource();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        // Auto-connect if wanted
        // ConnectToServer();
    }
    
    void Update()
    {
        // Process messages in the Unity main thread
        ProcessMessageQueue();
    }
    
    void OnDestroy()
    {
        Disconnect();
    }
    
    private void ProcessMessageQueue()
    {
        if (messageQueue.Count > 0)
        {
            Dictionary<string, object> message = null;
            lock (lockObject)
            {
                if (messageQueue.Count > 0)
                {
                    message = messageQueue.Dequeue();
                }
            }
            
            if (message != null)
            {
                // Dispatch the message to the appropriate handler
                string messageType = message.ContainsKey("type") ? message["type"].ToString() : "";
                
                // General message handler
                OnMessageReceived?.Invoke(message);
                
                // Specific handlers
                switch (messageType)
                {
                    case "room_list":
                        OnRoomListReceived?.Invoke(message);
                        break;
                    case "room_joined":
                        currentRoomId = message.ContainsKey("room_id") ? message["room_id"].ToString() : "";
                        OnRoomJoined?.Invoke(message);
                        break;
                    case "room_created":
                        currentRoomId = message.ContainsKey("room_id") ? message["room_id"].ToString() : "";
                        OnRoomCreated?.Invoke(message);
                        break;
                    case "player_joined":
                        OnPlayerJoined?.Invoke(message);
                        break;
                    case "game_started":
                        OnGameStarted?.Invoke(message);
                        break;
                    case "player_left":
                        OnPlayerLeft?.Invoke(message);
                        break;
                }
            }
        }
    }
    
    public void ConnectToServer()
    {
        if (isConnected)
        {
            Debug.Log("Already connected to server");
            return;
        }
        
        try
        {
            Debug.Log($"Connecting to {serverAddress}:{serverPort}");
            
            // Create TCP client
            tcpClient = new TcpClient();
            tcpClient.Connect(serverAddress, serverPort);
            
            if (useTls)
            {
                // Configure and use SSL/TLS
                sslStream = new SslStream(
                    tcpClient.GetStream(),
                    false,
                    new RemoteCertificateValidationCallback(ValidateServerCertificate),
                    null
                );
                
                try
                {
                    // The server name should match the name in the server's certificate
                    sslStream.AuthenticateAsClient(serverAddress);
                    Debug.Log("TLS authentication successful");
                    
                    // Start receiving thread with SSL stream
                    receiveThread = new Thread(new ThreadStart(ReceiveDataThread));
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }
                catch (Exception e)
                {
                    // If TLS fails, try with non-encrypted connection
                    Debug.LogError($"TLS authentication failed: {e.Message}. Falling back to non-encrypted connection.");
                    sslStream.Dispose();
                    sslStream = null;
                    
                    useTls = false;
                    ConnectWithoutTLS();
                }
            }
            else
            {
                ConnectWithoutTLS();
            }
            
            isConnected = true;
            Debug.Log("Connected to server");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error connecting to server: {e.Message}");
            Disconnect();
        }
    }
    
    private void ConnectWithoutTLS()
    {
        networkStream = tcpClient.GetStream();
        
        receiveThread = new Thread(new ThreadStart(ReceiveDataThread));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }
    
    private bool ValidateServerCertificate(
        object sender,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // For development, accept all certificates
        return true;
        
        // For production
        // return sslPolicyErrors == SslPolicyErrors.None;
    }
    
    private void ReceiveDataThread()
    {
        byte[] receiveBuffer = new byte[8192];
        StringBuilder messageBuilder = new StringBuilder();
        int bytesRead;
        
        try
        {
            while (isConnected && tcpClient != null && tcpClient.Connected)
            {
                // Read data from the appropriate stream
                Stream stream = useTls ? (Stream)sslStream : networkStream;
                
                if (!stream.CanRead)
                {
                    Debug.LogError("Stream is not readable. Disconnecting.");
                    Disconnect();
                    break;
                }
                
                bytesRead = stream.Read(receiveBuffer, 0, receiveBuffer.Length);
                
                if (bytesRead <= 0)
                {
                    // Connection closed by the server
                    Debug.Log("Server closed connection");
                    Disconnect();
                    break;
                }
                
                // Convert received bytes to string
                string message = Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
                
                // Process complete JSON messages
                messageBuilder.Append(message);
                ProcessCompleteMessages(messageBuilder);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in receive thread: {e.Message}");
            Disconnect();
        }
    }
    
    private void ProcessCompleteMessages(StringBuilder builder)
    {
        string data = builder.ToString();
        int startIndex = 0;
        int braceCount = 0;
        bool inQuotes = false;
        bool escaped = false;
        
        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];
            
            if (escaped)
            {
                // Skip the escaped character
                escaped = false;
                continue;
            }
            
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            
            if (inQuotes)
            {
                continue;
            }
            
            if (c == '{')
            {
                if (braceCount == 0)
                {
                    startIndex = i;
                }
                braceCount++;
            }
            else if (c == '}')
            {
                braceCount--;
                
                if (braceCount == 0)
                {
                    // Found a complete JSON object
                    string jsonMessage = data.Substring(startIndex, i - startIndex + 1);
                    
                    try
                    {
                        Dictionary<string, object> messageObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonMessage);
                        
                        // Store session ID if this is a welcome message
                        if (messageObj.ContainsKey("session_id") && sessionId == "")
                        {
                            sessionId = messageObj["session_id"].ToString();
                            Debug.Log($"Session ID: {sessionId}");
                        }
                        
                        // Add to queue for processing in main thread
                        lock (lockObject)
                        {
                            messageQueue.Enqueue(messageObj);
                        }
                        
                        if (showDebugLogs)
                        {
                            Debug.Log($"Received: {jsonMessage}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error parsing JSON: {e.Message}. Json: {jsonMessage}");
                    }
                    
                    // Cut out the processed message
                    builder.Remove(0, i + 1);
                    data = builder.ToString();
                    i = -1; // Reset the index
                    braceCount = 0;
                }
            }
        }
    }
    
    public void Disconnect()
    {
        try
        {
            isConnected = false;
            
            // Cancel any pending operations
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
            }
            
            // Close streams
            if (sslStream != null)
            {
                sslStream.Close();
                sslStream.Dispose();
                sslStream = null;
            }
            
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream.Dispose();
                networkStream = null;
            }
            
            // Close client
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
            
            // Wait for receive thread to exit
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(100);
                receiveThread = null;
            }
            
            // Reset state
            sessionId = "";
            currentRoomId = "";
            
            Debug.Log("Disconnected from server");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during disconnect: {e.Message}");
        }
    }
    
    public void SendMessage(Dictionary<string, object> message)
    {
        if (!isConnected || tcpClient == null || !tcpClient.Connected)
        {
            Debug.LogError("Not connected to server");
            return;
        }
        
        try
        {
            // Add session ID to message if available
            if (!string.IsNullOrEmpty(sessionId) && !message.ContainsKey("session_id"))
            {
                message["session_id"] = sessionId;
            }
            
            // Add room ID to message if available and appropriate
            if (!string.IsNullOrEmpty(currentRoomId) && 
                (message.ContainsKey("command") && 
                 (message["command"].ToString() == "CHAT" || 
                  message["command"].ToString() == "START_GAME" || 
                  message["command"].ToString() == "LEAVE_ROOM")))
            {
                message["room_id"] = currentRoomId;
            }
            
            string json = JsonConvert.SerializeObject(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            
            if (showDebugLogs)
            {
                Debug.Log($"Sending: {json}");
            }
            
            // Send through the appropriate stream
            if (useTls && sslStream != null)
            {
                sslStream.Write(data, 0, data.Length);
                sslStream.Flush();
            }
            else if (networkStream != null)
            {
                networkStream.Write(data, 0, data.Length);
                networkStream.Flush();
            }
            else
            {
                Debug.LogError("No valid stream available for sending data");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending message: {e.Message}");
            Disconnect();
        }
    }
    
    // Helper methods for common commands
    
    public void Authenticate(string username, string password)
    {
        var message = new Dictionary<string, object>
        {
            { "command", "AUTHENTICATE" },
            { "username", username },
            { "password", password }
        };
        
        SendMessage(message);
    }
    
    public void CreateRoom(string roomName, int maxPlayers = 10)
    {
        var message = new Dictionary<string, object>
        {
            { "command", "CREATE_ROOM" },
            { "room_name", roomName },
            { "max_players", maxPlayers }
        };
        
        SendMessage(message);
    }
    
    public void JoinRoom(string roomId)
    {
        var message = new Dictionary<string, object>
        {
            { "command", "JOIN_ROOM" },
            { "room_id", roomId }
        };
        
        SendMessage(message);
    }
    
    public void GetRoomList()
    {
        var message = new Dictionary<string, object>
        {
            { "command", "GET_ROOMS" }
        };
        
        SendMessage(message);
    }
    
    public void StartGame()
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            Debug.LogError("Not in a room");
            return;
        }
        
        var message = new Dictionary<string, object>
        {
            { "command", "START_GAME" },
            { "room_id", currentRoomId }
        };
        
        SendMessage(message);
    }
    
    public void LeaveRoom()
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            Debug.LogError("Not in a room");
            return;
        }
        
        var message = new Dictionary<string, object>
        {
            { "command", "LEAVE_ROOM" },
            { "room_id", currentRoomId }
        };
        
        SendMessage(message);
        currentRoomId = "";
    }
    
    public void SendChatMessage(string text)
    {
        if (string.IsNullOrEmpty(currentRoomId))
        {
            Debug.LogError("Not in a room");
            return;
        }
        
        var message = new Dictionary<string, object>
        {
            { "command", "CHAT" },
            { "room_id", currentRoomId },
            { "text", text }
        };
        
        SendMessage(message);
    }
    
    // Getters
    public bool IsConnected()
    {
        return isConnected && tcpClient != null && tcpClient.Connected;
    }
    
    public string GetSessionId()
    {
        return sessionId;
    }
    
    public string GetCurrentRoomId()
    {
        return currentRoomId;
    }
}