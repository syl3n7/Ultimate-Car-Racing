# Console Command System Setup Guide

This guide explains how to set up and use the simple console command system for changing server connection settings in the Ultimate Car Racing game.

## Features

- **Simple Command Interface**: Type commands like `connect(192.168.1.1)` and press Enter
- **Fast and Lightweight**: Minimal UI overhead, designed for quick server changes
- **Toggle Visibility**: Press ` (backtick) key or click button to show/hide console
- **Real-time Connection Control**: Change server address and port without restarting

## Quick Setup

### Automatic Setup (Recommended)

1. Open Unity Editor
2. Go to **Tools > Setup Console Command System**
3. The console UI will be automatically created and configured
4. Press ` (backtick) key to test the console

### Manual Setup

If you prefer manual setup:

1. Add `ConsoleCommandSystem.cs` script to any GameObject in your scene
2. Create a UI Canvas if one doesn't exist
3. Create the console UI elements and assign them to the script fields
4. Use the `ConsoleSetup.cs` script as reference for UI structure

## Usage

### Opening the Console

- **Keyboard**: Press ` (backtick/grave accent) key
- **Button**: Click the "Console" button in the top-left corner

### Available Commands

| Command | Description | Example |
|---------|-------------|---------|
| `connect(ip)` | Set server IP address | `connect(192.168.1.1)` |
| `connect(hostname)` | Set server hostname | `connect(localhost)` |
| `port(number)` | Set server port | `port(443)` |
| `status` | Show connection info | `status` |
| `disconnect` | Disconnect from server | `disconnect` |
| `help` | Show all commands | `help` |
| `clear` | Clear console output | `clear` |

### Command Examples

```
> connect(192.168.1.100)
Server address set to: 192.168.1.100
Note: Changes will take effect on next connection.

> port(8080)
Server port set to: 8080
Note: Changes will take effect on next connection.

> status
=== Connection Status ===
Server: 192.168.1.100:8080
Connected: false
Authenticated: false
Player: Player

> connect(game-server.example.com)
Server address set to: game-server.example.com
```

## How It Works

1. **Command Input**: Type commands in the input field and press Enter
2. **Command Processing**: The system parses commands using regex patterns
3. **Server Settings**: Commands directly modify `SecureNetworkManager.Instance.serverHost` and `serverPort`
4. **Real-time Feedback**: All output is shown in the console with confirmation messages

## Integration

The console system integrates with the existing `SecureNetworkManager`:

- **Server Host**: Changes `SecureNetworkManager.Instance.serverHost`
- **Server Port**: Changes `SecureNetworkManager.Instance.serverPort`
- **Connection Status**: Reads current connection state
- **Disconnect**: Calls `SecureNetworkManager.Instance.Disconnect()`

## Customization

### Keyboard Shortcut

Change the toggle key in the `ConsoleCommandSystem` component:

```csharp
public KeyCode toggleKey = KeyCode.BackQuote; // Change to your preferred key
```

### UI Appearance

Modify the UI elements created by `ConsoleSetup.cs`:

- **Console Panel**: Background color, size, position
- **Output Text**: Font, size, color
- **Input Field**: Background, placeholder text
- **Toggle Button**: Position, size, colors

### Adding New Commands

Add new commands in the `ProcessCommand` method:

```csharp
else if (lowerCommand.StartsWith("yourcommand(") && lowerCommand.EndsWith(")"))
{
    ProcessYourCommand(command);
}
```

## Troubleshooting

### Console Won't Open

- Check that `ConsoleCommandSystem` component is active in the scene
- Verify the console panel GameObject is assigned
- Make sure the Canvas is properly set up

### Commands Not Working

- Verify `SecureNetworkManager.Instance` is available
- Check Unity Console for error messages
- Make sure command syntax is correct (use parentheses)

### UI Layout Issues

- Check Canvas scaling settings
- Verify RectTransform anchoring in the console panel
- Adjust console panel size in the Inspector

## Technical Details

### File Structure

```
Assets/Scripts/
├── ConsoleCommandSystem.cs    # Main console logic
└── ConsoleSetup.cs           # Editor setup utility
```

### Dependencies

- **Unity UI System**: Canvas, InputField, Text, Button
- **SecureNetworkManager**: For server connection management
- **System.Text.RegularExpressions**: For command parsing

### Performance

- **Minimal Impact**: Only active when console is visible
- **No Update Overhead**: Uses event-driven input handling
- **Memory Efficient**: Limited output history (20 lines by default)

## Tips

1. **Quick Testing**: Use `status` command to verify server settings
2. **Local Development**: Use `connect(localhost)` for local server testing
3. **Remote Servers**: Use IP addresses or hostnames for remote connections
4. **Port Configuration**: Most servers use port 443 for TLS connections
5. **Connection Changes**: Settings take effect on next connection attempt

This console system provides a fast, simple way to change server connections without needing to modify code or restart the application.
