# UDP Encryption Testing Utilities

This directory contains testing and diagnostic utilities for the UDP encryption system.

## Files

### NetworkEncryptionDiagnostic.cs
- **Purpose**: Real-time monitoring of UDP encryption status
- **Usage**: Attach to any GameObject in your scene
- **Features**: 
  - GUI overlay showing encryption statistics
  - F9 hotkey for detailed diagnostics
  - Real-time packet monitoring
- **When to use**: During development and debugging to verify encryption is working

### UdpEncryptionTester.cs
- **Purpose**: Interactive testing utility with UI buttons
- **Usage**: Attach to a GameObject with UI buttons
- **Features**:
  - Manual encryption/decryption testing
  - Connection testing with visual feedback
  - Interactive UI for quick testing
- **When to use**: For manual testing and demonstration purposes

### UdpEncryptionVerifier.cs
- **Purpose**: Automated unit tests for encryption system
- **Usage**: Attach to any GameObject and call `RunAllTests()`
- **Features**:
  - Key derivation verification
  - Packet format validation
  - Encryption/decryption roundtrip testing
- **When to use**: For automated testing and CI/CD pipelines

## Usage Notes

These testing utilities can be safely removed from production builds. They are designed for development and debugging purposes only.

For production builds, consider removing these files or using preprocessor directives to exclude them:
```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Testing code here
#endif
```
