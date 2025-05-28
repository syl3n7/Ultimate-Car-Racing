using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quick testing utility for UDP encryption functionality
/// Provides easy buttons to test connection and encryption status
/// </summary>
public class UdpEncryptionTester : MonoBehaviour
{
    [Header("UI References")]
    public Button connectButton;
    public Button authenticateButton;
    public Button testUdpButton;
    public Button disconnectButton;
    public Text statusText;
    public Text logText;
    
    [Header("Test Settings")]
    public string testServerHost = "localhost";
    public int testServerPort = 443;
    public string testPlayerName = "TestPlayer";
    public string testPlayerPassword = "test123";
    
    private SecureNetworkManager networkManager;
    private string logBuffer = "";
    
    void Start()
    {
        networkManager = SecureNetworkManager.Instance;
        if (networkManager == null)
        {
            Debug.LogError("SecureNetworkManager not found!");
            return;
        }
        
        SetupUI();
        SetupEventHandlers();
        
        UpdateStatus("Ready to test UDP encryption");
    }
    
    void SetupUI()
    {
        if (connectButton) connectButton.onClick.AddListener(TestConnection);
        if (authenticateButton) authenticateButton.onClick.AddListener(TestAuthentication);
        if (testUdpButton) testUdpButton.onClick.AddListener(TestUdpEncryption);
        if (disconnectButton) disconnectButton.onClick.AddListener(TestDisconnection);
        
        // Set initial button states
        if (authenticateButton) authenticateButton.interactable = false;
        if (testUdpButton) testUdpButton.interactable = false;
        if (disconnectButton) disconnectButton.interactable = false;
    }
    
    void SetupEventHandlers()
    {
        if (networkManager != null)
        {
            networkManager.OnConnected += OnConnected;
            networkManager.OnConnectionFailed += OnConnectionFailed;
            networkManager.OnAuthenticationChanged += OnAuthenticationChanged;
        }
    }
    
    void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnConnected -= OnConnected;
            networkManager.OnConnectionFailed -= OnConnectionFailed;
            networkManager.OnAuthenticationChanged -= OnAuthenticationChanged;
        }
    }
    
    public async void TestConnection()
    {
        UpdateStatus("Testing connection...");
        AddLog("🔗 Starting connection test");
        
        // Configure network manager
        networkManager.serverHost = testServerHost;
        networkManager.serverPort = testServerPort;
        networkManager.playerName = testPlayerName;
        networkManager.playerPassword = testPlayerPassword;
        
        bool success = await networkManager.ConnectToServer();
        
        if (success)
        {
            AddLog("✅ Connection test successful");
            if (connectButton) connectButton.interactable = false;
            if (authenticateButton) authenticateButton.interactable = true;
            if (disconnectButton) disconnectButton.interactable = true;
        }
        else
        {
            AddLog("❌ Connection test failed");
            UpdateStatus("Connection failed");
        }
    }
    
    public void TestAuthentication()
    {
        AddLog("🔐 Authentication should happen automatically after connection");
        AddLog("Check console for NAME_OK response and UDP encryption setup");
    }
    
    public async void TestUdpEncryption()
    {
        AddLog("📡 Testing UDP encryption...");
        
        // Test sending some UDP packets
        for (int i = 0; i < 3; i++)
        {
            Vector3 testPosition = new Vector3(i * 10f, 0f, 0f);
            Quaternion testRotation = Quaternion.identity;
            
            await networkManager.SendPositionUpdate(testPosition, testRotation);
            AddLog($"Sent test position update #{i + 1}");
            
            await System.Threading.Tasks.Task.Delay(100); // Small delay between sends
        }
        
        // Test input updates
        for (int i = 0; i < 3; i++)
        {
            float steering = UnityEngine.Random.Range(-1f, 1f);
            float throttle = UnityEngine.Random.Range(0f, 1f);
            float brake = UnityEngine.Random.Range(0f, 1f);
            
            await networkManager.SendInputUpdate(steering, throttle, brake);
            AddLog($"Sent test input update #{i + 1}");
            
            await System.Threading.Tasks.Task.Delay(100);
        }
        
        AddLog("✅ UDP test packets sent - check console for encryption status");
    }
    
    public async void TestDisconnection()
    {
        AddLog("🔌 Disconnecting...");
        await networkManager.Disconnect();
        
        // Reset button states
        if (connectButton) connectButton.interactable = true;
        if (authenticateButton) authenticateButton.interactable = false;
        if (testUdpButton) testUdpButton.interactable = false;
        if (disconnectButton) disconnectButton.interactable = false;
        
        UpdateStatus("Disconnected");
        AddLog("✅ Disconnected successfully");
    }
    
    private void OnConnected(string message)
    {
        AddLog($"📡 Connected: {message}");
        UpdateStatus("Connected - waiting for authentication");
    }
    
    private void OnConnectionFailed(string error)
    {
        AddLog($"❌ Connection failed: {error}");
        UpdateStatus("Connection failed");
    }
    
    private void OnAuthenticationChanged(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            AddLog("🔐 Authentication successful - UDP encryption should be available");
            UpdateStatus("Authenticated with UDP encryption");
            if (testUdpButton) testUdpButton.interactable = true;
        }
        else
        {
            AddLog("❌ Authentication failed");
            UpdateStatus("Authentication failed");
            if (testUdpButton) testUdpButton.interactable = false;
        }
    }
    
    private void UpdateStatus(string status)
    {
        if (statusText) statusText.text = $"Status: {status}";
        Debug.Log($"[UdpEncryptionTester] {status}");
    }
    
    private void AddLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string logEntry = $"[{timestamp}] {message}";
        
        logBuffer += logEntry + "\n";
        
        // Keep only last 20 lines
        string[] lines = logBuffer.Split('\n');
        if (lines.Length > 20)
        {
            logBuffer = string.Join("\n", lines, lines.Length - 20, 20);
        }
        
        if (logText) logText.text = logBuffer;
        Debug.Log($"[UdpEncryptionTester] {message}");
    }
    
    void OnGUI()
    {
        // Simple GUI fallback if UI components are not assigned
        if (connectButton == null)
        {
            GUILayout.BeginArea(new Rect(10, 100, 300, 400));
            GUILayout.Label("UDP Encryption Tester", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Test Connection"))
                TestConnection();
            
            if (GUILayout.Button("Test Authentication"))
                TestAuthentication();
            
            if (GUILayout.Button("Test UDP Encryption"))
                TestUdpEncryption();
            
            if (GUILayout.Button("Disconnect"))
                TestDisconnection();
                
            GUILayout.Label("Check console for detailed logs");
            
            GUILayout.EndArea();
        }
    }
}
