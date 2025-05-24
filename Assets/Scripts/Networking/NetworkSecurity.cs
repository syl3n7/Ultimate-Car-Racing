using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Handles UDP packet encryption and decryption using AES-256-CBC as specified in the server documentation
/// </summary>
public class NetworkSecurity
{
    private readonly bool _encryptionEnabled;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _encryptionIV;

    /// <summary>
    /// Creates a new instance of NetworkSecurity with encryption disabled
    /// </summary>
    public NetworkSecurity()
    {
        _encryptionEnabled = false;
        _encryptionKey = null;
        _encryptionIV = null;
    }

    /// <summary>
    /// Creates a new instance of NetworkSecurity with encryption enabled
    /// </summary>
    /// <param name="sessionId">The session ID from the server</param>
    /// <param name="password">The player's password</param>
    public NetworkSecurity(string sessionId, string password)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(password))
        {
            Debug.LogError("Cannot initialize encryption with empty sessionId or password");
            _encryptionEnabled = false;
            return;
        }

        try
        {
            // Derive encryption key from session ID and password (as described in 13.2)
            // This is a simplified implementation - in a production environment, use a proper KDF
            string combinedSecret = sessionId + "|" + password;
            
            // Generate the encryption key using SHA-256
            using (var sha256 = SHA256.Create())
            {
                _encryptionKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedSecret));
            }

            // Generate a fixed IV based on the session ID
            // In a production environment, use a unique IV for each message
            _encryptionIV = new byte[16]; // AES requires a 16-byte IV
            byte[] sessionBytes = Encoding.UTF8.GetBytes(sessionId);
            for (int i = 0; i < Math.Min(sessionBytes.Length, 16); i++)
            {
                _encryptionIV[i] = sessionBytes[i];
            }

            _encryptionEnabled = true;
            Debug.Log("UDP encryption initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize encryption: {ex.Message}");
            _encryptionEnabled = false;
        }
    }

    /// <summary>
    /// Indicates whether encryption is enabled for this instance
    /// </summary>
    public bool IsEncryptionEnabled => _encryptionEnabled;

    /// <summary>
    /// Creates a packet from the given data, encrypting it if encryption is enabled
    /// </summary>
    /// <typeparam name="T">The type of data to send</typeparam>
    /// <param name="data">The data to send</param>
    /// <returns>The formatted packet data ready to send</returns>
    public byte[] CreatePacket<T>(T data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        // Convert to JSON
        string jsonData = JsonConvert.SerializeObject(data);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonData);

        // If encryption is not enabled, return plain text
        if (!_encryptionEnabled)
            return jsonBytes;

        try
        {
            // Encrypt the data using AES-256-CBC
            byte[] encryptedData;
            using (var aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.IV = _encryptionIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                using (var memoryStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(jsonBytes, 0, jsonBytes.Length);
                        cryptoStream.FlushFinalBlock();
                    }

                    encryptedData = memoryStream.ToArray();
                }
            }

            // Format the packet as described in 13.2: [4-byte length][encrypted data]
            byte[] lengthBytes = BitConverter.GetBytes(encryptedData.Length);
            byte[] packet = new byte[4 + encryptedData.Length];
            Buffer.BlockCopy(lengthBytes, 0, packet, 0, 4);
            Buffer.BlockCopy(encryptedData, 0, packet, 4, encryptedData.Length);

            return packet;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to encrypt packet: {ex.Message}");
            return jsonBytes; // Fall back to plain text if encryption fails
        }
    }

    /// <summary>
    /// Parses a packet, decrypting it if necessary
    /// </summary>
    /// <typeparam name="T">The expected type of data</typeparam>
    /// <param name="packetData">The received packet data</param>
    /// <returns>The parsed data</returns>
    public T ParsePacket<T>(byte[] packetData)
    {
        if (packetData == null || packetData.Length == 0)
            throw new ArgumentException("Packet data cannot be null or empty", nameof(packetData));
            
        try
        {
            // Check if this looks like an encrypted packet (has length header)
            if (_encryptionEnabled && packetData.Length >= 4)
            {
                // Extract the length from the first 4 bytes
                int dataLength = BitConverter.ToInt32(packetData, 0);
                
                // Ensure the packet is valid
                if (dataLength > 0 && dataLength <= packetData.Length - 4)
                {
                    // Extract and decrypt the data
                    byte[] encryptedData = new byte[dataLength];
                    Buffer.BlockCopy(packetData, 4, encryptedData, 0, dataLength);
                    
                    byte[] decryptedData;
                    using (var aes = Aes.Create())
                    {
                        aes.Key = _encryptionKey;
                        aes.IV = _encryptionIV;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;

                        using (var decryptor = aes.CreateDecryptor())
                        using (var memoryStream = new MemoryStream(encryptedData))
                        using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                        using (var resultStream = new MemoryStream())
                        {
                            cryptoStream.CopyTo(resultStream);
                            decryptedData = resultStream.ToArray();
                        }
                    }

                    // Parse the decrypted JSON
                    string jsonStr = Encoding.UTF8.GetString(decryptedData);
                    return JsonConvert.DeserializeObject<T>(jsonStr);
                }
            }
            
            // Fall back to plain text parsing
            string plainText = Encoding.UTF8.GetString(packetData).TrimEnd('\n');
            return JsonConvert.DeserializeObject<T>(plainText);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse packet: {ex.Message}");
            return default;
        }
    }
}