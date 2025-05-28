using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Diagnostic utility to verify UDP encryption is properly enabled and functioning
/// Helps identify if UDP encryption is actually being used during gameplay
/// </summary>
public class NetworkEncryptionDiagnostic : MonoBehaviour
{
    [Header("Diagnostic Settings")]
    public bool enableRealTimeMonitoring = true;
    public bool logEncryptedPackets = false;
    public KeyCode diagnosticKey = KeyCode.F9;
    
    private bool _lastAuthStatus = false;
    private bool _lastEncryptionStatus = false;
    private int _encryptedPacketsSent = 0;
    private int _plainPacketsSent = 0;
    private float _lastDiagnosticTime = 0f;
    
    void Update()
    {
        if (Input.GetKeyDown(diagnosticKey))
        {
            RunFullDiagnostic();
        }
        
        if (enableRealTimeMonitoring && Time.time - _lastDiagnosticTime > 5f)
        {
            _lastDiagnosticTime = Time.time;
            CheckEncryptionStatus();
        }
    }
    
    [ContextMenu("Run Full Diagnostic")]
    public void RunFullDiagnostic()
    {
        Debug.Log("=== Network Encryption Diagnostic ===");
        
        CheckSecureNetworkManager();
        CheckAuthenticationStatus();
        CheckUdpEncryptionStatus();
        CheckConnectionFlow();
        ProvideRecommendations();
        
        Debug.Log("=== End Diagnostic ===");
    }
    
    private void CheckSecureNetworkManager()
    {
        Debug.Log("Checking SecureNetworkManager instance...");
        
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager == null)
        {
            Debug.LogError("❌ SecureNetworkManager.Instance is null!");
            return;
        }
        
        Debug.Log("✅ SecureNetworkManager instance found");
        
        // Check connection status using reflection to access private fields
        var type = networkManager.GetType();
        var isConnectedField = type.GetField("_isConnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var isAuthenticatedField = type.GetField("_isAuthenticated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var sessionIdField = type.GetField("_sessionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var udpCryptoField = type.GetField("_udpCrypto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (isConnectedField != null)
        {
            bool isConnected = (bool)isConnectedField.GetValue(networkManager);
            Debug.Log($"Connection status: {(isConnected ? "✅ Connected" : "❌ Not connected")}");
        }
        
        if (isAuthenticatedField != null)
        {
            bool isAuthenticated = (bool)isAuthenticatedField.GetValue(networkManager);
            Debug.Log($"Authentication status: {(isAuthenticated ? "✅ Authenticated" : "❌ Not authenticated")}");
        }
        
        if (sessionIdField != null)
        {
            string sessionId = (string)sessionIdField.GetValue(networkManager);
            Debug.Log($"Session ID: {(string.IsNullOrEmpty(sessionId) ? "❌ Not set" : "✅ " + sessionId)}");
        }
        
        if (udpCryptoField != null)
        {
            var udpCrypto = udpCryptoField.GetValue(networkManager);
            Debug.Log($"UDP Encryption: {(udpCrypto == null ? "❌ Not initialized" : "✅ Initialized")}");
        }
    }
    
    private void CheckAuthenticationStatus()
    {
        Debug.Log("Checking authentication flow...");
        
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager == null) return;
        
        string playerName = networkManager.playerName;
        string playerPassword = networkManager.playerPassword;
        
        Debug.Log($"Player name: {(string.IsNullOrEmpty(playerName) ? "❌ Not set" : "✅ " + playerName)}");
        Debug.Log($"Player password: {(string.IsNullOrEmpty(playerPassword) ? "❌ Not set" : "✅ [Set]")}");
        
        if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(playerPassword))
        {
            Debug.LogWarning("⚠️ Player credentials not configured - UDP encryption will not be enabled");
        }
    }
    
    private void CheckUdpEncryptionStatus()
    {
        Debug.Log("Checking UDP encryption implementation...");
        
        // Check if UdpEncryption class exists and is properly implemented
        try
        {
            var testCrypto = new UdpEncryption("test_session");
            var testData = new { command = "TEST", data = "test" };
            var packet = testCrypto.CreatePacket(testData);
            var parsed = testCrypto.ParsePacket<Dictionary<string, object>>(packet);
            
            if (parsed != null && parsed.ContainsKey("command") && parsed["command"].ToString() == "TEST")
            {
                Debug.Log("✅ UdpEncryption class is working correctly");
            }
            else
            {
                Debug.LogError("❌ UdpEncryption class failed basic test");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ UdpEncryption class error: {ex.Message}");
        }
    }
    
    private void CheckConnectionFlow()
    {
        Debug.Log("Checking connection flow requirements...");
        
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager == null) return;
        
        // Check server settings
        Debug.Log($"Server host: {networkManager.serverHost}");
        Debug.Log($"Server port: {networkManager.serverPort}");
        
        if (networkManager.serverPort != 443)
        {
            Debug.LogWarning("⚠️ Server port is not 443 - ensure this matches your server configuration");
        }
        
        // Verify event handlers are set up
        var onConnectedField = networkManager.GetType().GetField("OnConnected");
        if (onConnectedField != null)
        {
            var onConnected = (System.Action<string>)onConnectedField.GetValue(networkManager);
            Debug.Log($"OnConnected event: {(onConnected == null ? "❌ No handlers" : "✅ Has handlers")}");
        }
    }
    
    private void CheckEncryptionStatus()
    {
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager == null) return;
        
        var type = networkManager.GetType();
        var isAuthenticatedField = type.GetField("_isAuthenticated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var udpCryptoField = type.GetField("_udpCrypto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (isAuthenticatedField != null && udpCryptoField != null)
        {
            bool isAuthenticated = (bool)isAuthenticatedField.GetValue(networkManager);
            var udpCrypto = udpCryptoField.GetValue(networkManager);
            bool hasEncryption = udpCrypto != null;
            
            // Log changes in status
            if (isAuthenticated != _lastAuthStatus)
            {
                Debug.Log($"Authentication status changed: {isAuthenticated}");
                _lastAuthStatus = isAuthenticated;
            }
            
            if (hasEncryption != _lastEncryptionStatus)
            {
                Debug.Log($"UDP encryption status changed: {hasEncryption}");
                _lastEncryptionStatus = hasEncryption;
            }
            
            // Show status in console if encryption is not working
            if (isAuthenticated && !hasEncryption)
            {
                Debug.LogWarning("⚠️ Authenticated but UDP encryption not initialized!");
            }
        }
    }
    
    private void ProvideRecommendations()
    {
        Debug.Log("=== Recommendations ===");
        
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager == null)
        {
            Debug.Log("1. Ensure SecureNetworkManager is present in the scene");
            return;
        }
        
        var type = networkManager.GetType();
        var isConnectedField = type.GetField("_isConnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var isAuthenticatedField = type.GetField("_isAuthenticated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var udpCryptoField = type.GetField("_udpCrypto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        bool isConnected = isConnectedField != null && (bool)isConnectedField.GetValue(networkManager);
        bool isAuthenticated = isAuthenticatedField != null && (bool)isAuthenticatedField.GetValue(networkManager);
        bool hasEncryption = udpCryptoField != null && udpCryptoField.GetValue(networkManager) != null;
        
        if (!isConnected)
        {
            Debug.Log("1. Connect to the server first using ConnectToServer()");
        }
        else if (!isAuthenticated)
        {
            Debug.Log("1. Ensure player name and password are set correctly");
            Debug.Log("2. Check if the server responds with NAME_OK and authenticated=true");
        }
        else if (!hasEncryption)
        {
            Debug.Log("1. Verify server responds with udpEncryption=true in NAME_OK message");
            Debug.Log("2. Check HandleNameOk method is being called correctly");
            Debug.Log("3. Verify UdpEncryption constructor is working");
        }
        else
        {
            Debug.Log("✅ UDP encryption appears to be properly configured!");
            Debug.Log("Monitor UDP traffic to verify encrypted packets are being sent");
        }
    }
    
    void OnGUI()
    {
        if (!enableRealTimeMonitoring) return;
        
        GUI.Label(new Rect(10, 10, 300, 20), "UDP Encryption Status (Press " + diagnosticKey + " for full diagnostic)");
        
        var networkManager = SecureNetworkManager.Instance;
        if (networkManager != null)
        {
            var type = networkManager.GetType();
            var isConnectedField = type.GetField("_isConnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var isAuthenticatedField = type.GetField("_isAuthenticated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var udpCryptoField = type.GetField("_udpCrypto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            bool isConnected = isConnectedField != null && (bool)isConnectedField.GetValue(networkManager);
            bool isAuthenticated = isAuthenticatedField != null && (bool)isAuthenticatedField.GetValue(networkManager);
            bool hasEncryption = udpCryptoField != null && udpCryptoField.GetValue(networkManager) != null;
            
            GUI.Label(new Rect(10, 30, 200, 20), $"Connected: {(isConnected ? "✅" : "❌")}");
            GUI.Label(new Rect(10, 50, 200, 20), $"Authenticated: {(isAuthenticated ? "✅" : "❌")}");
            GUI.Label(new Rect(10, 70, 200, 20), $"UDP Encrypted: {(hasEncryption ? "✅" : "❌")}");
            
            if (isAuthenticated && hasEncryption)
            {
                GUI.color = Color.green;
                GUI.Label(new Rect(10, 90, 250, 20), "🔒 UDP encryption is ACTIVE");
            }
            else if (isAuthenticated && !hasEncryption)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(10, 90, 250, 20), "⚠️ Authenticated but encryption OFF");
            }
            else
            {
                GUI.color = Color.red;
                GUI.Label(new Rect(10, 90, 250, 20), "🔓 UDP encryption is OFF");
            }
            GUI.color = Color.white;
        }
    }
}
