using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Net;
using TMPro;

public class LobbyController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject lobbyPanel;
    public GameObject roomPanel;
    public Button hostButton;
    public Button refreshButton;
    public Button startButton;
    public Button leaveButton;
    public TMP_InputField playerNameInput;
    public Transform serverListContent;
    public GameObject serverEntryPrefab;
    public TMP_Text roomInfoText;
    public TMP_Text connectionStatusText;
    public TMP_Text publicIdText;

    [Header("Settings")]
    public float serverRefreshInterval = 5f;
    public string gameSceneName = "GameScene";

    private Dictionary<string, ServerInfo> availableServers = new Dictionary<string, ServerInfo>();
    private float lastRefreshTime;
    private string localPlayerName = "Player";

    private class ServerInfo
    {
        public string serverId;
        public string serverName;
        public string hostName;
        public int playerCount;
        public int maxPlayers;
    }

    void Awake()
    {
        // Initialize UI
        lobbyPanel.SetActive(true);
        roomPanel.SetActive(false);
        
        // Load player name from prefs
        localPlayerName = PlayerPrefs.GetString("PlayerName", "Player");
        playerNameInput.text = localPlayerName;
        
        // Setup button listeners
        hostButton.onClick.AddListener(OnHostClicked);
        refreshButton.onClick.AddListener(RefreshServerList);
        startButton.onClick.AddListener(OnStartClicked);
        leaveButton.onClick.AddListener(OnLeaveClicked);
        
        playerNameInput.onEndEdit.AddListener(OnNameChanged);
    }

    void Start()
    {
        // Register network events
        NetworkManager.Instance.OnNetworkMessageReceived += HandleNetworkMessage;
        
        // Initial refresh
        RefreshServerList();
    }

    void Update()
    {
        // Periodically refresh server list
        if (Time.time - lastRefreshTime > serverRefreshInterval)
        {
            RefreshServerList();
        }

        // Update connection status
        UpdateConnectionStatus();
        
        // Update public ID display
        if (publicIdText != null && !string.IsNullOrEmpty(NetworkManager.Instance.publicEndPoint))
        {
            publicIdText.text = $"Your ID: {NetworkManager.Instance.publicEndPoint.Split(':')[1]}";
        }
    }

    void OnDestroy()
    {
        // Clean up network events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnNetworkMessageReceived -= HandleNetworkMessage;
        }
    }

    void OnNameChanged(string newName)
    {
        localPlayerName = newName;
        PlayerPrefs.SetString("PlayerName", localPlayerName);
    }

    void OnHostClicked()
    {
        NetworkManager.Instance.HostGame();
        
        // Switch to room view
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
        
        // Update room info
        UpdateRoomInfo(new List<string> { localPlayerName });
    }

    void OnStartClicked()
    {
        // Tell all peers to load the game scene
        NetworkManager.Instance.SendToAll("LOAD_SCENE|" + gameSceneName);
        
        // Load locally
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
    }

    void OnLeaveClicked()
    {
        // If we're host, disband the room
        NetworkManager.Instance.SendToAll("ROOM_CLOSED");
        
        // Return to lobby
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
        
        // Re-register with relay
        NetworkManager.Instance.RegisterWithRelay();
    }

    void RefreshServerList()
    {
        // Request server list from relay
        NetworkManager.Instance.SendToRelay("LIST_SERVERS");
        lastRefreshTime = Time.time;
    }

    void UpdateConnectionStatus()
    {
        if (connectionStatusText == null) return;

        int connectedCount = NetworkManager.Instance.connectedPeers.Count;
        if (connectedCount > 0)
        {
            connectionStatusText.text = $"Connected to {connectedCount} peer(s)";
            connectionStatusText.color = Color.green;
        }
        else if (roomPanel.activeSelf)
        {
            connectionStatusText.text = "Connecting...";
            connectionStatusText.color = Color.yellow;
        }
        else
        {
            connectionStatusText.text = "Not connected";
            connectionStatusText.color = Color.red;
        }
    }

    void UpdateRoomInfo(List<string> playerNames)
    {
        if (roomInfoText == null) return;

        string info = $"Room Host: {localPlayerName}\nPlayers:\n";
        foreach (string name in playerNames)
        {
            info += $"- {name}\n";
        }

        roomInfoText.text = info;
    }

    void HandleNetworkMessage(string command, string[] args, IPEndPoint endpoint)
    {
        switch (command)
        {
            case "SERVER_LIST":
                HandleServerList(args);
                break;
                
            case "PLAYER_JOINED":
                HandlePlayerJoined(args);
                break;
                
            case "PLAYER_LIST":
                HandlePlayerList(args);
                break;
                
            case "LOAD_SCENE":
                HandleLoadScene(args);
                break;
                
            case "ROOM_CLOSED":
                HandleRoomClosed();
                break;
        }
    }

    void HandleServerList(string[] servers)
    {
        // Clear current list
        availableServers.Clear();
        
        // Parse server list (format: id|name|host|players/max)
        for (int i = 0; i < servers.Length; i += 4)
        {
            var info = new ServerInfo
            {
                serverId = servers[i],
                serverName = servers[i+1],
                hostName = servers[i+2],
                playerCount = int.Parse(servers[i+3].Split('/')[0]),
                maxPlayers = int.Parse(servers[i+3].Split('/')[1])
            };
            
            availableServers[info.serverId] = info;
        }
        
        // Update UI
        UpdateServerListUI();
    }

    void UpdateServerListUI()
    {
        // Clear existing entries
        foreach (Transform child in serverListContent)
        {
            Destroy(child.gameObject);
        }
        
        // Create new entries
        foreach (var server in availableServers.Values)
        {
            GameObject entry = Instantiate(serverEntryPrefab, serverListContent);
            ServerListEntry entryScript = entry.GetComponent<ServerListEntry>();
            
            if (entryScript != null)
            {
                entryScript.Setup(
                    server.serverName,
                    $"{server.hostName}'s Game",
                    $"{server.playerCount}/{server.maxPlayers}",
                    () => JoinSelectedServer(server.serverId)
                );
            }
        }
    }

    void JoinSelectedServer(string serverId)
    {
        NetworkManager.Instance.JoinGame(serverId);
        
        // Switch to room view
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(true);
    }

    void HandlePlayerJoined(string[] args)
    {
        // args: playerName
        if (args.Length < 1) return;
        
        // Request updated player list
        string endpoint = null;
        NetworkManager.Instance.SendToPeer(endpoint, "REQUEST_PLAYERS");
    }

    void HandlePlayerList(string[] args)
    {
        // args: name1,name2,name3
        if (args.Length < 1) return;
        
        var playerNames = new List<string>(args[0].Split(','));
        UpdateRoomInfo(playerNames);
    }

    void HandleLoadScene(string[] args)
    {
        if (args.Length < 1) return;
        UnityEngine.SceneManagement.SceneManager.LoadScene(args[0]);
    }

    void HandleRoomClosed()
    {
        // Return to lobby
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
    }

    // Helper method to send to all connected peers
    void SendToAll(string message)
    {
        foreach (var peer in NetworkManager.Instance.connectedPeers)
        {
            NetworkManager.Instance.SendToPeer(peer.peerId, message);
        }
    }
}

internal partial class ServerListEntry
{
    public void Setup(string serverServerName, string s, string s1, Action action)
    {
        throw new NotImplementedException();
    }
}