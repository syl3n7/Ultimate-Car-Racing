using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Simple console command system for changing server connection settings
/// Type commands like "connect(192.168.1.1)" and press Enter
/// </summary>
public class ConsoleCommandSystem : MonoBehaviour
{
    [Header("Console UI")]
    public GameObject consolePanel;
    public InputField commandInput;
    public Text outputText;
    public Button toggleButton;
    
    [Header("Settings")]
    public KeyCode toggleKey = KeyCode.BackQuote; // ` key
    public int maxOutputLines = 20;
    
    private bool isConsoleVisible = false;
    private List<string> outputHistory = new List<string>();
    
    void Start()
    {
        // Initialize console state
        if (consolePanel != null)
        {
            consolePanel.SetActive(false);
        }
        
        // Setup input field
        if (commandInput != null)
        {
            commandInput.onEndEdit.AddListener(OnCommandEntered);
        }
        
        // Setup toggle button
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleConsole);
        }
        
        // Welcome message
        AddOutput("Console ready. Type 'help' for commands.");
    }
    
    void Update()
    {
        // Toggle console with key
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleConsole();
        }
        
        // Keep focus on input when console is open
        if (isConsoleVisible && commandInput != null && !commandInput.isFocused)
        {
            commandInput.ActivateInputField();
        }
    }
    
    void ToggleConsole()
    {
        isConsoleVisible = !isConsoleVisible;
        
        if (consolePanel != null)
        {
            consolePanel.SetActive(isConsoleVisible);
        }
        
        if (isConsoleVisible && commandInput != null)
        {
            commandInput.text = "";
            commandInput.ActivateInputField();
        }
    }
    
    void OnCommandEntered(string command)
    {
        if (string.IsNullOrEmpty(command.Trim()))
            return;
        
        // Add command to output
        AddOutput($"> {command}");
        
        // Process the command
        ProcessCommand(command.Trim());
        
        // Clear input
        if (commandInput != null)
        {
            commandInput.text = "";
            commandInput.ActivateInputField();
        }
    }
    
    void ProcessCommand(string command)
    {
        try
        {
            // Convert to lowercase for easier matching
            string lowerCommand = command.ToLower();
            
            if (lowerCommand == "help")
            {
                ShowHelp();
            }
            else if (lowerCommand == "clear")
            {
                ClearOutput();
            }
            else if (lowerCommand == "status")
            {
                ShowConnectionStatus();
            }
            else if (lowerCommand.StartsWith("connect(") && lowerCommand.EndsWith(")"))
            {
                ProcessConnectCommand(command);
            }
            else if (lowerCommand.StartsWith("port(") && lowerCommand.EndsWith(")"))
            {
                ProcessPortCommand(command);
            }
            else if (lowerCommand == "disconnect")
            {
                ProcessDisconnectCommand();
            }
            else
            {
                AddOutput($"Unknown command: {command}. Type 'help' for available commands.");
            }
        }
        catch (System.Exception ex)
        {
            AddOutput($"Error processing command: {ex.Message}");
        }
    }
    
    void ProcessConnectCommand(string command)
    {
        // Extract IP address from connect(ip) format
        Match match = Regex.Match(command, @"connect\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string ipAddress = match.Groups[1].Value.Trim();
            
            // Basic IP validation
            if (IsValidIPAddress(ipAddress))
            {
                SetServerAddress(ipAddress);
                AddOutput($"Server address set to: {ipAddress}");
                AddOutput("Use 'status' to see current connection info.");
            }
            else
            {
                AddOutput($"Invalid IP address: {ipAddress}");
                AddOutput("Example: connect(192.168.1.1) or connect(localhost)");
            }
        }
        else
        {
            AddOutput("Invalid connect command format.");
            AddOutput("Usage: connect(ip_address)");
            AddOutput("Example: connect(192.168.1.1)");
        }
    }
    
    void ProcessPortCommand(string command)
    {
        // Extract port number from port(number) format
        Match match = Regex.Match(command, @"port\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string portStr = match.Groups[1].Value.Trim();
            
            if (int.TryParse(portStr, out int port))
            {
                if (port > 0 && port <= 65535)
                {
                    SetServerPort(port);
                    AddOutput($"Server port set to: {port}");
                }
                else
                {
                    AddOutput("Port must be between 1 and 65535");
                }
            }
            else
            {
                AddOutput($"Invalid port number: {portStr}");
            }
        }
        else
        {
            AddOutput("Invalid port command format.");
            AddOutput("Usage: port(number)");
            AddOutput("Example: port(443)");
        }
    }
    
    void ProcessDisconnectCommand()
    {
        if (SecureNetworkManager.Instance != null)
        {
            if (SecureNetworkManager.Instance.IsConnected())
            {
                _ = SecureNetworkManager.Instance.Disconnect();
                AddOutput("Disconnecting from server...");
            }
            else
            {
                AddOutput("Not currently connected to server.");
            }
        }
        else
        {
            AddOutput("NetworkManager not available.");
        }
    }
    
    void SetServerAddress(string address)
    {
        if (SecureNetworkManager.Instance != null)
        {
            SecureNetworkManager.Instance.serverHost = address;
            AddOutput("Note: Changes will take effect on next connection.");
        }
        else
        {
            AddOutput("NetworkManager not available.");
        }
    }
    
    void SetServerPort(int port)
    {
        if (SecureNetworkManager.Instance != null)
        {
            SecureNetworkManager.Instance.serverPort = port;
            AddOutput("Note: Changes will take effect on next connection.");
        }
        else
        {
            AddOutput("NetworkManager not available.");
        }
    }
    
    bool IsValidIPAddress(string ip)
    {
        // Allow localhost
        if (ip.ToLower() == "localhost")
            return true;
        
        // Simple IP validation (basic check)
        if (System.Net.IPAddress.TryParse(ip, out _))
            return true;
        
        // Allow hostname format (basic check)
        if (Regex.IsMatch(ip, @"^[a-zA-Z0-9.-]+$"))
            return true;
        
        return false;
    }
    
    void ShowHelp()
    {
        AddOutput("=== Console Commands ===");
        AddOutput("help - Show this help message");
        AddOutput("connect(ip) - Set server IP address");
        AddOutput("  Example: connect(192.168.1.1)");
        AddOutput("  Example: connect(localhost)");
        AddOutput("port(number) - Set server port");
        AddOutput("  Example: port(443)");
        AddOutput("status - Show current connection status");
        AddOutput("disconnect - Disconnect from server");
        AddOutput("clear - Clear console output");
        AddOutput($"Press '{toggleKey}' or button to toggle console");
    }
    
    void ShowConnectionStatus()
    {
        if (SecureNetworkManager.Instance != null)
        {
            var networkManager = SecureNetworkManager.Instance;
            AddOutput("=== Connection Status ===");
            AddOutput($"Server: {networkManager.serverHost}:{networkManager.serverPort}");
            AddOutput($"Connected: {networkManager.IsConnected()}");
            AddOutput($"Authenticated: {networkManager.IsAuthenticated()}");
            AddOutput($"Player: {networkManager.playerName}");
            
            if (networkManager.IsConnected())
            {
                AddOutput($"Session ID: {networkManager.GetClientId()}");
                string roomId = networkManager.GetCurrentRoomId();
                if (!string.IsNullOrEmpty(roomId))
                {
                    AddOutput($"Room: {roomId}");
                }
            }
        }
        else
        {
            AddOutput("NetworkManager not available.");
        }
    }
    
    void ClearOutput()
    {
        outputHistory.Clear();
        UpdateOutputDisplay();
    }
    
    void AddOutput(string message)
    {
        outputHistory.Add(message);
        
        // Limit history size
        while (outputHistory.Count > maxOutputLines)
        {
            outputHistory.RemoveAt(0);
        }
        
        UpdateOutputDisplay();
        
        // Also log to Unity console for debugging
        Debug.Log($"[Console] {message}");
    }
    
    void UpdateOutputDisplay()
    {
        if (outputText != null)
        {
            outputText.text = string.Join("\n", outputHistory);
        }
    }
}
