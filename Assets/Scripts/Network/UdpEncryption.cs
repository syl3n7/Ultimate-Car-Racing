using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-performance UDP encryption for MP-Server protocol
/// Implements AES-256-CBC encryption with session-specific keys
/// Full compliance with MP-Server security standards
/// </summary>
public class UdpEncryption
{
    private readonly byte[] _key;
    private readonly byte[] _iv;
    
    public UdpEncryption(string sessionId, string sharedSecret = "RacingServerUDP2024!")
    {
        // Generate deterministic key and IV from session ID and shared secret
        using var sha256 = SHA256.Create();
        var keySource = Encoding.UTF8.GetBytes(sessionId + sharedSecret);
        var keyHash = sha256.ComputeHash(keySource);
        
        _key = new byte[32]; // AES-256 key
        _iv = new byte[16];   // AES IV
        
        Array.Copy(keyHash, 0, _key, 0, 32);
        Array.Copy(keyHash, 16, _iv, 0, 16);
    }
    
    public byte[] Encrypt(string jsonData)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        var dataBytes = Encoding.UTF8.GetBytes(jsonData);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
    }
    
    public string Decrypt(byte[] encryptedData)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    /// <summary>
    /// Create encrypted packet with length header (MP-Server format)
    /// </summary>
    public byte[] CreatePacket(object data)
    {
        var json = JsonUtility.ToJson(data);
        var encryptedData = Encrypt(json);
        
        // Create packet with length header
        var packet = new byte[4 + encryptedData.Length];
        BitConverter.GetBytes(encryptedData.Length).CopyTo(packet, 0);
        encryptedData.CopyTo(packet, 4);
        
        return packet;
    }
    
    /// <summary>
    /// Parse encrypted packet and deserialize (MP-Server format)
    /// </summary>
    public T ParsePacket<T>(byte[] packetData)
    {
        if (packetData.Length < 4)
            return default(T);
        
        var length = BitConverter.ToInt32(packetData, 0);
        if (length != packetData.Length - 4 || length <= 0)
            return default(T);
        
        var encryptedData = new byte[length];
        Array.Copy(packetData, 4, encryptedData, 0, length);
        
        var json = Decrypt(encryptedData);
        if (string.IsNullOrEmpty(json))
            return default(T);
        
        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception)
        {
            return default(T);
        }
    }
    
    /// <summary>
    /// Verify encryption is working correctly
    /// </summary>
    public bool TestEncryption()
    {
        try
        {
            var testData = new { command = "TEST", timestamp = DateTime.UtcNow };
            var packet = CreatePacket(testData);
            var parsed = ParsePacket<Dictionary<string, object>>(packet);
            
            return parsed != null && parsed.ContainsKey("command") && 
                   parsed["command"].ToString() == "TEST";
        }
        catch
        {
            return false;
        }
    }
}
