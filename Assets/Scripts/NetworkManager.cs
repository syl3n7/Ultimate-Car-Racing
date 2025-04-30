using UnityEngine;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Text;

[DefaultExecutionOrder(-100)] // Ensures early execution
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    public int localPort = 7777;
    public string relayServerIP = "19.116.114.89.rev.vodafone.pt";
    public int relayPort = 7778;
    public float connectionTimeout = 10f;
    public float heartbeatInterval = 30f;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public List<ConnectedPeer> connectedPeers = new List<ConnectedPeer>();

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning;
    private float lastHeartbeatTime;
    public string publicEndPoint;
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object queueLock = new object();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeNetwork();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void InitializeNetwork()
    {
        try
        {
            udpClient = new UdpClient(localPort);
            isRunning = true;
            receiveThread = new Thread(ReceiveThread);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            RegisterWithRelay();
            Log("Network initialized on port " + localPort);
        }
        catch (Exception e)
        {
            LogError($"Network initialization failed: {e.Message}");
        }
    }

    void Update()
    {
        // Process main thread actions
        lock (queueLock)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue()?.Invoke();
            }
        }

        // Send periodic heartbeats
        if (Time.time - lastHeartbeatTime > heartbeatInterval)
        {
            SendHeartbeat();
            lastHeartbeatTime = Time.time;
        }
    }

    void ReceiveThread()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data);

                ProcessRawMessage(message, remoteEP);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted)
            {
                // Expected when shutting down
            }
            catch (Exception e)
            {
                LogError($"Receive error: {e.Message}");
            }
        }
    }

    void ProcessRawMessage(string message, IPEndPoint remoteEP)
    {
        string[] parts = message.Split('|');
        if (parts.Length == 0) return;

        string command = parts[0];
        string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        switch (command)
        {
            case "REGISTERED":
                publicEndPoint = $"{args[0]}:{args[1]}";
                Log($"Registered with relay. Public endpoint: {publicEndPoint}");
                break;

            case "PEER_INFO":
                HandlePeerInfo(args[0], args[1], int.Parse(args[2]), args[3], int.Parse(args[4]));
                break;

            case "PUNCH":
                HandlePunchRequest(IPAddress.Parse(args[0]), int.Parse(args[1]));
                break;

            case "PUNCH_ACK":
                HandlePunchAck(IPAddress.Parse(args[0]), int.Parse(args[1]));
                break;

            default:
                // Forward game messages to main thread
                EnqueueAction(() => OnNetworkMessageReceived?.Invoke(command, args, remoteEP));
                break;
        }
    }

    void HandlePeerInfo(string peerId, string publicIP, int publicPort, string localIP, int localPort)
    {
        var peer = new IPEndPoint(IPAddress.Parse(publicIP), publicPort);
        var localPeer = new IPEndPoint(IPAddress.Parse(localIP), localPort);

        EnqueueAction(() =>
        {
            if (!connectedPeers.Exists(p => p.peerId == peerId))
            {
                connectedPeers.Add(new ConnectedPeer
                {
                    peerId = peerId,
                    publicEndPoint = peer,
                    localEndPoint = localPeer,
                    lastSeen = Time.time
                });
            }

            AttemptPunchthrough(peerId, peer, localPeer);
        });
    }

    void AttemptPunchthrough(string peerId, IPEndPoint publicEP, IPEndPoint localEP)
    {
        Log($"Attempting NAT punchthrough to {peerId}...");

        // Try public endpoint
        SendPunch(publicEP);

        // Try local endpoint if different
        if (!publicEP.Equals(localEP))
        {
            SendPunch(localEP);
        }

        // Start timeout
        StartCoroutine(PunchthroughTimeout(peerId));
    }

    void SendPunch(IPEndPoint endpoint)
    {
        try
        {
            string message = $"PUNCH|{LocalIPAddress()}|{localPort}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, endpoint);
        }
        catch (Exception e)
        {
            LogError($"Punch send failed: {e.Message}");
        }
    }

    IEnumerator PunchthroughTimeout(string peerId)
    {
        float startTime = Time.time;
        while (Time.time - startTime < connectionTimeout)
        {
            yield return null;
        }

        var peer = connectedPeers.Find(p => p.peerId == peerId);
        if (peer != null && !peer.isConnected)
        {
            Log($"Punchthrough to {peerId} failed, using relay");
            peer.useRelay = true;
        }
    }

    void HandlePunchRequest(IPAddress remoteIP, int remotePort)
    {
        var remoteEP = new IPEndPoint(remoteIP, remotePort);
        Log($"Received punch from {remoteEP}");

        // Respond with acknowledgement
        try
        {
            string message = $"PUNCH_ACK|{LocalIPAddress()}|{localPort}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, remoteEP);
        }
        catch (Exception e)
        {
            LogError($"Punch response failed: {e.Message}");
        }
    }

    void HandlePunchAck(IPAddress remoteIP, int remotePort)
    {
        var remoteEP = new IPEndPoint(remoteIP, remotePort);
        Log($"Punchthrough succeeded with {remoteEP}");

        EnqueueAction(() =>
        {
            var peer = connectedPeers.Find(p => 
                p.publicEndPoint.Equals(remoteEP) || 
                (p.localEndPoint != null && p.localEndPoint.Equals(remoteEP)));

            if (peer != null)
            {
                peer.isConnected = true;
                peer.useRelay = false;
                peer.lastSeen = Time.time;
            }
        });
    }

    public void RegisterWithRelay()
    {
        try
        {
            string message = $"REGISTER|{LocalIPAddress()}|{localPort}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, relayServerIP, relayPort);
        }
        catch (Exception e)
        {
            LogError($"Relay registration failed: {e.Message}");
        }
    }

    void SendHeartbeat()
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes("HEARTBEAT");
            udpClient.Send(data, data.Length, relayServerIP, relayPort);
        }
        catch (Exception e)
        {
            LogError($"Heartbeat failed: {e.Message}");
        }
    }

    public void HostGame()
    {
        try
        {
            string message = $"HOST|{publicEndPoint}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, relayServerIP, relayPort);
            Log("Game hosted successfully");
        }
        catch (Exception e)
        {
            LogError($"Host failed: {e.Message}");
        }
    }

    public void JoinGame(string hostId)
    {
        try
        {
            string message = $"JOIN|{hostId}|{publicEndPoint}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, relayServerIP, relayPort);
            Log($"Attempting to join game {hostId}");
        }
        catch (Exception e)
        {
            LogError($"Join failed: {e.Message}");
        }
    }

    public void SendToPeer(string peerId, string message)
    {
        var peer = connectedPeers.Find(p => p.peerId == peerId);
        if (peer == null) 
        {
            // If we don't have a specific peerId, try to find the first connected peer
            // This helps with handling null peerId in HandlePlayerJoined
            if (string.IsNullOrEmpty(peerId) && connectedPeers.Count > 0)
            {
                peer = connectedPeers[0];
            }
            else
            {
                return;
            }
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            
            if (peer.isConnected && !peer.useRelay)
            {
                // Send directly
                udpClient.Send(data, data.Length, peer.publicEndPoint);
            }
            else
            {
                // Send via relay
                string relayMsg = $"RELAY|{peer.peerId}|{message}";
                byte[] relayData = Encoding.UTF8.GetBytes(relayMsg);
                udpClient.Send(relayData, relayData.Length, relayServerIP, relayPort);
            }
        }
        catch (Exception e)
        {
            LogError($"Send to peer failed: {e.Message}");
        }
    }

    // Implement the missing SendToAll method
    public void SendToAll(string message)
    {
        try
        {
            foreach (var peer in connectedPeers)
            {
                SendToPeer(peer.peerId, message);
            }
            Log($"Message sent to all peers: {message}");
        }
        catch (Exception e)
        {
            LogError($"SendToAll failed: {e.Message}");
        }
    }

    // Implement the missing SendToRelay method
    public void SendToRelay(string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, relayServerIP, relayPort);
            Log($"Message sent to relay: {message}");
        }
        catch (Exception e)
        {
            LogError($"SendToRelay failed: {e.Message}");
        }
    }

    void EnqueueAction(Action action)
    {
        lock (queueLock)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    string LocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }

    void OnDestroy()
    {
        isRunning = false;
        receiveThread?.Interrupt();
        udpClient?.Close();
    }

    void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[Network] {message}");
    }

    void LogError(string message)
    {
        Debug.LogError($"[Network] {message}");
    }

    // Events
    public event Action<string, string[], IPEndPoint> OnNetworkMessageReceived;

    [System.Serializable]
    public class ConnectedPeer
    {
        public string peerId;
        public IPEndPoint publicEndPoint;
        public IPEndPoint localEndPoint;
        public bool isConnected;
        public bool useRelay;
        public float lastSeen;
    }
}