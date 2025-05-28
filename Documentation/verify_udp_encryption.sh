#!/bin/bash

# UDP Encryption Quick Verification Script
# Run this script to quickly check if UDP encryption is properly set up

echo "🔒 UDP Encryption Quick Verification"
echo "===================================="
echo ""

# Check if required files exist
echo "📁 Checking files..."

required_files=(
    "Assets/Scripts/SecureNetworkManager.cs"
    "Assets/Scripts/UdpEncryption.cs" 
    "Assets/Scripts/NetworkEncryptionDiagnostic.cs"
    "Assets/Scripts/UdpEncryptionVerifier.cs"
    "Assets/Scripts/UdpEncryptionTester.cs"
    "UDP_ENCRYPTION_SETUP.md"
    "UDP_ENCRYPTION_IMPLEMENTATION_SUMMARY.md"
)

all_files_exist=true

for file in "${required_files[@]}"; do
    if [ -f "$file" ]; then
        echo "✅ $file"
    else
        echo "❌ $file (MISSING)"
        all_files_exist=false
    fi
done

echo ""

if [ "$all_files_exist" = true ]; then
    echo "✅ All required files are present!"
else
    echo "❌ Some files are missing. Please check the implementation."
    exit 1
fi

echo ""
echo "📋 Quick Setup Checklist"
echo "========================"
echo ""
echo "In Unity Editor:"
echo "1. [ ] SecureNetworkManager component added to scene"
echo "2. [ ] Server host/port configured correctly"
echo "3. [ ] Player name and password set"
echo "4. [ ] enableDebugLogs = true for testing"
echo ""
echo "Optional diagnostic components:"
echo "5. [ ] NetworkEncryptionDiagnostic - for real-time monitoring"
echo "6. [ ] UdpEncryptionVerifier - for unit tests"
echo "7. [ ] UdpEncryptionTester - for interactive testing"
echo ""
echo "🔧 Testing Commands"
echo "==================="
echo ""
echo "In Unity Console (or via components):"
echo ""
echo "// Test encryption algorithm"
echo "var verifier = FindObjectOfType<UdpEncryptionVerifier>();"
echo "verifier.RunVerificationTests();"
echo ""
echo "// Test server compatibility"
echo "verifier.TestServerCompatibility();"
echo ""
echo "// Check real-time status (press F9 in play mode)"
echo "var diagnostic = FindObjectOfType<NetworkEncryptionDiagnostic>();"
echo "diagnostic.RunFullDiagnostic();"
echo ""
echo "🔍 What to Look For"
echo "==================="
echo ""
echo "✅ WORKING (secure):"
echo "  - 🔒 Sending encrypted position update (XX bytes)"
echo "  - 🔒 Received encrypted UDP message (XX bytes)"
echo "  - Status overlay shows: UDP Encrypted: ✅"
echo ""
echo "❌ NOT WORKING (security risk):"
echo "  - 🔓 Sending UNENCRYPTED position update - Security Risk!"
echo "  - 🔓 Received UNENCRYPTED UDP message - Security Risk!"
echo "  - Status overlay shows: UDP Encrypted: ❌"
echo ""
echo "📖 Documentation"
echo "================="
echo ""
echo "For detailed setup instructions:"
echo "📄 UDP_ENCRYPTION_SETUP.md"
echo ""
echo "For implementation summary:"
echo "📄 UDP_ENCRYPTION_IMPLEMENTATION_SUMMARY.md"
echo ""
echo "For server protocol details:"
echo "📄 Server-Docs.md"
echo ""
echo "🎯 Verification Complete!"
echo ""
echo "If all files are present, follow the Unity setup steps above."
echo "Monitor the console logs to verify encryption is working."
echo ""
echo "🔒 Security Status: READY FOR TESTING"
