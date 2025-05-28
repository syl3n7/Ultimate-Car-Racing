using System;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Verification script for UDP encryption implementation
/// Ensures compliance with server documentation requirements
/// </summary>
public class UdpEncryptionVerifier : MonoBehaviour
{
    [Header("Test Configuration")]
    public bool runTestsOnStart = false;
    public string testSessionId = "test_session_123";
    
    void Start()
    {
        if (runTestsOnStart)
        {
            RunVerificationTests();
        }
    }
    
    [ContextMenu("Run UDP Encryption Tests")]
    public void RunVerificationTests()
    {
        Debug.Log("=== UDP Encryption Verification Tests ===");
        
        try
        {
            TestKeyDerivation();
            TestPacketFormat();
            TestEncryptionDecryption();
            TestJsonSerialization();
            TestPacketSizeValidation();
            
            Debug.Log("✅ All UDP encryption tests passed!");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ UDP encryption test failed: {ex.Message}");
        }
    }
    
    private void TestKeyDerivation()
    {
        Debug.Log("Testing key derivation...");
        
        // Test with known session ID
        var crypto = new UdpEncryption(testSessionId);
        
        // The key should be consistently generated
        var crypto2 = new UdpEncryption(testSessionId);
        
        // Both should encrypt/decrypt the same way
        string testData = "test data";
        var encrypted1 = crypto.Encrypt(testData);
        var decrypted2 = crypto2.Decrypt(encrypted1);
        
        if (decrypted2 != testData)
        {
            throw new Exception("Key derivation inconsistent between instances");
        }
        
        Debug.Log("✅ Key derivation test passed");
    }
    
    private void TestPacketFormat()
    {
        Debug.Log("Testing packet format compliance...");
        
        var crypto = new UdpEncryption(testSessionId);
        
        var testUpdate = new
        {
            command = "UPDATE",
            sessionId = testSessionId,
            position = new { x = 1.0f, y = 2.0f, z = 3.0f },
            rotation = new { x = 0.0f, y = 0.0f, z = 0.0f, w = 1.0f }
        };
        
        byte[] packet = crypto.CreatePacket(testUpdate);
        
        // Verify packet has length header
        if (packet.Length < 4)
        {
            throw new Exception("Packet too small - missing length header");
        }
        
        // Verify length header is correct
        int expectedLength = BitConverter.ToInt32(packet, 0);
        int actualDataLength = packet.Length - 4;
        
        if (expectedLength != actualDataLength)
        {
            throw new Exception($"Length header mismatch: expected {expectedLength}, actual {actualDataLength}");
        }
        
        Debug.Log($"✅ Packet format test passed - packet size: {packet.Length} bytes");
    }
    
    private void TestEncryptionDecryption()
    {
        Debug.Log("Testing encryption/decryption roundtrip...");
        
        var crypto = new UdpEncryption(testSessionId);
        
        var originalData = new
        {
            command = "INPUT",
            sessionId = testSessionId,
            roomId = "test_room",
            input = new
            {
                steering = 0.5f,
                throttle = 0.8f,
                brake = 0.0f,
                timestamp = 123.456f
            },
            client_id = testSessionId
        };
        
        // Create encrypted packet
        byte[] packet = crypto.CreatePacket(originalData);
        
        // Parse it back
        var parsedData = crypto.ParsePacket<dynamic>(packet);
        
        if (parsedData == null)
        {
            throw new Exception("Failed to parse encrypted packet");
        }
        
        // Verify command field
        string command = parsedData.command.ToString();
        if (command != "INPUT")
        {
            throw new Exception($"Command field corrupted: expected 'INPUT', got '{command}'");
        }
        
        Debug.Log("✅ Encryption/decryption roundtrip test passed");
    }
    
    private void TestJsonSerialization()
    {
        Debug.Log("Testing JSON serialization compatibility...");
        
        var crypto = new UdpEncryption(testSessionId);
        
        // Test with Unity Vector3/Quaternion-like data
        var testData = new
        {
            command = "UPDATE",
            sessionId = testSessionId,
            position = new { x = 10.5f, y = -2.3f, z = 7.8f },
            rotation = new { x = 0.1f, y = 0.2f, z = 0.3f, w = 0.9f }
        };
        
        // Serialize and encrypt
        string json = JsonConvert.SerializeObject(testData);
        byte[] encrypted = crypto.Encrypt(json);
        
        // Decrypt and deserialize
        string decryptedJson = crypto.Decrypt(encrypted);
        var reconstructed = JsonConvert.DeserializeObject(decryptedJson);
        
        if (reconstructed == null)
        {
            throw new Exception("JSON serialization failed");
        }
        
        Debug.Log("✅ JSON serialization compatibility test passed");
    }
    
    private void TestPacketSizeValidation()
    {
        Debug.Log("Testing packet size validation...");
        
        var crypto = new UdpEncryption(testSessionId);
        
        // Test with malformed packets
        byte[] tooSmall = new byte[2];
        var result1 = crypto.ParsePacket<dynamic>(tooSmall);
        if (result1 != null)
        {
            throw new Exception("Should reject packets smaller than 4 bytes");
        }
        
        // Test with invalid length header
        byte[] invalidLength = new byte[10];
        BitConverter.GetBytes(20).CopyTo(invalidLength, 0); // Claims 20 bytes but only has 6 data bytes
        var result2 = crypto.ParsePacket<dynamic>(invalidLength);
        if (result2 != null)
        {
            throw new Exception("Should reject packets with invalid length headers");
        }
        
        Debug.Log("✅ Packet size validation test passed");
    }
    
    [ContextMenu("Test Server Compatibility")]
    public void TestServerCompatibility()
    {
        Debug.Log("=== Server Compatibility Test ===");
        
        var crypto = new UdpEncryption(testSessionId);
        
        // Create a packet exactly as described in server docs
        var serverCompatibleUpdate = new
        {
            command = "UPDATE",
            sessionId = testSessionId,
            position = new { x = 66.0, y = -2.0, z = 0.8 }, // From server docs spawn position
            rotation = new { x = 0.0, y = 0.0, z = 0.0, w = 1.0 }
        };
        
        byte[] packet = crypto.CreatePacket(serverCompatibleUpdate);
        
        Debug.Log($"Server-compatible packet created:");
        Debug.Log($"- Total size: {packet.Length} bytes");
        Debug.Log($"- Header: {BitConverter.ToInt32(packet, 0)} bytes");
        Debug.Log($"- Format: [4-byte length][{packet.Length - 4} bytes encrypted JSON]");
        
        // Verify we can parse it back
        var parsed = crypto.ParsePacket<dynamic>(packet);
        if (parsed == null)
        {
            Debug.LogError("❌ Failed to parse server-compatible packet");
            return;
        }
        
        Debug.Log("✅ Server compatibility test passed");
        Debug.Log($"Parsed command: {parsed.command}");
    }
}
