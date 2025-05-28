# Ultimate Car Racing - Scripts

This directory contains all C# scripts for the Ultimate Car Racing multiplayer game, organized by functionality.

## Directory Structure

### 📁 Core/
Core gameplay scripts that handle fundamental game mechanics:
- `GameManager.cs` - Central game state management
- `CarController.cs` - Local player car physics and control
- `CameraFollow.cs` - Camera system following the player
- `RemotePlayerController.cs` - Remote player synchronization

### 📁 Network/
Networking and multiplayer communication scripts:
- `SecureNetworkManager.cs` - Main network manager with UDP encryption
- `UdpEncryption.cs` - UDP packet encryption/decryption utility
- `UnityMainThreadDispatcher.cs` - Thread-safe Unity operations

### 📁 UI/
User interface and menu management scripts:
- `UIManager.cs` - Main UI management and coordination
- `LoadingScreenManager.cs` - Loading screen functionality
- `LoadingScreenSetup.cs` - Loading screen configuration
- `RoomListItem.cs` - Multiplayer room list items

### 📁 Console/
In-game development console system:
- `ConsoleCommandSystem.cs` - Console command system implementation
- `ConsoleSetup.cs` - Console UI setup and integration

### 📁 Testing/
Development and testing utilities:
- `NetworkEncryptionDiagnostic.cs` - Real-time encryption monitoring
- `UdpEncryptionTester.cs` - Interactive encryption testing
- `UdpEncryptionVerifier.cs` - Automated encryption unit tests

## Key Features

### 🔒 Security
- **UDP Encryption**: All multiplayer traffic is encrypted using AES-256
- **Session Security**: Secure key derivation and session management
- **Security Monitoring**: Real-time encryption status and security warnings

### 🎮 Gameplay
- **Realistic Car Physics**: Advanced vehicle dynamics and control
- **Smooth Multiplayer**: Low-latency position synchronization
- **Camera System**: Dynamic camera following with smooth transitions

### 🛠️ Development Tools
- **Console Commands**: In-game server configuration and debugging
- **Encryption Testing**: Comprehensive testing suite for security validation
- **Real-time Diagnostics**: Live monitoring of network and encryption status

## Usage Guidelines

### For Development
1. Use scripts in `Testing/` directory for debugging and validation
2. Access console with `~` key for server configuration
3. Monitor encryption status with F9 diagnostic overlay

### For Production
1. Consider removing or securing `Console/` and `Testing/` directories
2. Ensure all network traffic uses encryption (check logs for warnings)
3. Validate server connection settings before deployment

## Dependencies

- **Unity 2022.3+** - Core Unity engine
- **Unity Netcode** - Basic networking foundation
- **.NET Cryptography** - For UDP encryption
- **Unity Input System** - For car controls and console input

## Security Notes

This implementation follows security best practices:
- All UDP packets are encrypted
- No sensitive data transmitted in plaintext
- Comprehensive security logging
- Session-based key management

For detailed security implementation, see `Documentation/UDP_ENCRYPTION_SETUP.md`.
