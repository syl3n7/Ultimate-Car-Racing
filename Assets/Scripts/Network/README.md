# Network Scripts

This directory contains all networking-related scripts for multiplayer functionality and secure communication.

## Files

### SecureNetworkManager.cs
- **Purpose**: Main network manager with UDP encryption support
- **Features**:
  - Secure UDP packet transmission
  - Player authentication and session management
  - Position and input synchronization
  - Hostname resolution and connection handling
  - Comprehensive encryption logging and debugging
- **Usage**: Attach to a NetworkManager GameObject
- **Security**: Implements encrypted UDP communication as per server requirements

### UdpEncryption.cs
- **Purpose**: UDP packet encryption and decryption utility
- **Features**:
  - AES-256 encryption for UDP packets
  - Key derivation from session credentials
  - Packet format handling (header + encrypted payload)
  - Security validation and integrity checks
- **Usage**: Used internally by SecureNetworkManager
- **Security**: Provides cryptographic protection for all UDP traffic

### UnityMainThreadDispatcher.cs
- **Purpose**: Thread-safe execution of Unity operations from network threads
- **Features**:
  - Main thread task queuing
  - Thread-safe Unity API access
  - Async operation support
  - Network callback handling
- **Usage**: Attach to a persistent GameObject
- **Note**: Essential for network operations that need to update Unity objects

## Architecture

The networking system follows this flow:
1. **SecureNetworkManager** handles all network connections and coordination
2. **UdpEncryption** encrypts/decrypts all UDP traffic for security
3. **UnityMainThreadDispatcher** ensures thread-safe Unity operations

## Security Implementation

- All UDP packets are encrypted using AES-256
- Session keys are derived from server authentication
- Unencrypted packets trigger security warnings
- Comprehensive logging for security auditing

## Dependencies

- System.Security.Cryptography for encryption
- Unity Networking for basic network operations
- Threading utilities for async operations

## Configuration

Network settings can be configured through:
- Console commands (see Console directory)
- Direct script configuration
- Runtime server connection changes
