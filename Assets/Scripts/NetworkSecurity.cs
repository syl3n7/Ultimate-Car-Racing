using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Handles security-related features for the networking system including UDP encryption and SSL certificate validation.
/// Implements the security requirements from SERVER-README.md sections 4.2 and 13.2.
/// </summary>
public class NetworkSecurity
{
    // Use AES-256-CBC as specified in SERVER-README.md section 13.2
    private byte[] _encryptionKey;
    private byte[] _encryptionIv;
    
    // Store session ID for derived keys
    private string _sessionId;
    
    // Flag to indicate if encryption is enabled
    private bool _encryptionEnabled = false;
    
    /// <summary>
    /// Creates a new NetworkSecurity instance with no encryption yet.
    /// Call SetupEncryption after authentication to enable UDP encryption.
    /// </summary>
    public NetworkSecurity()
    {
        Debug.Log("NetworkSecurity initialized");
    }
    
    /// <summary>
    /// Setup encryption using the session ID and a shared secret
    /// </summary>
    /// <param name="sessionId">The session ID received during authentication</param>
    /// <param name="sharedSecret">Optional additional shared secret</param>
    public void SetupEncryption(string sessionId, string sharedSecret = "")
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError("Cannot setup encryption with null or empty sessionId");
            return;
        }
        
        _sessionId = sessionId;
        
        // Derive keys from session ID and shared secret as per the protocol docs
        string keyMaterial = sessionId;
        if (!string.IsNullOrEmpty(sharedSecret))
        {
            keyMaterial += sharedSecret;
        }
        
        using (SHA256 sha256 = SHA256.Create())
        {
            // Create a 256-bit key (32 bytes) from the key material
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyMaterial);
            byte[] hashBytes = sha256.ComputeHash(keyBytes);
            
            // First 32 bytes for the key
            _encryptionKey = new byte[32];
            Array.Copy(hashBytes, 0, _encryptionKey, 0, 32);
            
            // Generate a new IV for each session (16 bytes for AES)
            _encryptionIv = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(_encryptionIv);
            }
        }
        
        _encryptionEnabled = true;
        Debug.Log("UDP encryption configured for session: " + sessionId);
    }
    
    /// <summary>
    /// Creates an encrypted packet for UDP transmission
    /// </summary>
    /// <param name="data">The data object to serialize and encrypt</param>
    /// <returns>Encrypted byte array with required header</returns>
    public byte[] CreatePacket(object data)
    {
        if (!_encryptionEnabled)
        {
            Debug.LogWarning("Encryption not configured! Sending unencrypted packet.");
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(data) + "\n");
        }
        
        try 
        {
            // Convert object to JSON string
            string jsonData = JsonUtility.ToJson(data);
            byte[] plainText = Encoding.UTF8.GetBytes(jsonData);
            
            // Encrypt using AES-256-CBC
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.IV = _encryptionIv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                
                using (MemoryStream ms = new MemoryStream())
                {
                    // Write IV at the beginning of the stream
                    ms.Write(_encryptionIv, 0, _encryptionIv.Length);
                    
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(plainText, 0, plainText.Length);
                        cs.FlushFinalBlock();
                    }
                    
                    byte[] encrypted = ms.ToArray();
                    
                    // Format according to protocol: [4-byte length][encrypted data]
                    using (MemoryStream packet = new MemoryStream())
                    {
                        // Write length as 4-byte header
                        byte[] lengthBytes = BitConverter.GetBytes(encrypted.Length);
                        packet.Write(lengthBytes, 0, 4);
                        
                        // Write encrypted data
                        packet.Write(encrypted, 0, encrypted.Length);
                        
                        return packet.ToArray();
                    }
                }
            }
        }
        catch (Exception ex) 
        {
            Debug.LogError($"Error creating encrypted packet: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Parses and decrypts an incoming UDP packet
    /// </summary>
    /// <param name="data">The encrypted data received</param>
    /// <returns>Decrypted JSON string or null if decryption fails</returns>
    public string ParsePacket(byte[] data)
    {
        if (!_encryptionEnabled)
        {
            Debug.LogWarning("Encryption not configured! Treating as unencrypted packet.");
            return Encoding.UTF8.GetString(data);
        }
        
        try
        {
            // Check if this looks like an encrypted packet (must be at least 4 bytes for length header)
            if (data.Length <= 4)
            {
                Debug.LogWarning("Packet too small to be encrypted, treating as plaintext");
                return Encoding.UTF8.GetString(data);
            }
            
            // Check packet format (first 4 bytes should be length)
            int declaredLength = BitConverter.ToInt32(data, 0);
            if (declaredLength != data.Length - 4)
            {
                Debug.LogWarning("Packet length header doesn't match actual data, may be plaintext");
                return Encoding.UTF8.GetString(data);
            }
            
            // Extract encrypted data (skip 4-byte header)
            byte[] encryptedData = new byte[data.Length - 4];
            Array.Copy(data, 4, encryptedData, 0, data.Length - 4);
            
            // Extract IV from the beginning of encryptedData (first 16 bytes)
            byte[] iv = new byte[16];
            Array.Copy(encryptedData, 0, iv, 0, 16);
            
            // Extract the actual encrypted content (everything after IV)
            byte[] encrypted = new byte[encryptedData.Length - 16];
            Array.Copy(encryptedData, 16, encrypted, 0, encrypted.Length);
            
            // Decrypt using AES-256-CBC
            using (Aes aes = Aes.Create())
            {
                aes.Key = _encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(encrypted, 0, encrypted.Length);
                        cs.FlushFinalBlock();
                    }
                    
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }
        catch (Exception ex) 
        {
            Debug.LogError($"Error decrypting packet: {ex.Message}");
            
            // If decryption fails, try to process as plaintext as a fallback
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }
    }
    
    /// <summary>
    /// Validates server certificates, implementing security guidelines from section 13.1.
    /// This should be used as a callback for SslStream.AuthenticateAsClient.
    /// </summary>
    /// <returns>True if certificate is valid or if development mode accepts self-signed certs</returns>
    public bool ValidateServerCertificate(
        object sender,
        X509Certificate certificate, 
        X509Chain chain, 
        SslPolicyErrors sslPolicyErrors)
    {
        // For development/LAN use with self-signed certificates
        #if DEVELOPMENT_BUILD || UNITY_EDITOR
        if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
        {
            Debug.LogWarning("Accepting self-signed certificate (Development mode)");
            return true;
        }
        #endif
        
        // For production, enforce proper certificate validation
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            Debug.LogError($"Certificate validation failed: {sslPolicyErrors}");
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Cleans up any resources used by the security system
    /// </summary>
    public void Cleanup()
    {
        // Clear sensitive data
        if (_encryptionKey != null)
        {
            Array.Clear(_encryptionKey, 0, _encryptionKey.Length);
        }
        
        if (_encryptionIv != null)
        {
            Array.Clear(_encryptionIv, 0, _encryptionIv.Length);
        }
        
        _sessionId = null;
        _encryptionEnabled = false;
    }
    
    /// <summary>
    /// Determines if a given packet is likely an encrypted packet
    /// </summary>
    public static bool IsLikelyEncryptedPacket(byte[] data)
    {
        // Check if the data meets the minimum requirements for an encrypted packet:
        // - At least 4 bytes for length header + 16 bytes for IV + 1 byte data
        // - First 4 bytes should represent a valid length
        if (data.Length <= 21)
        {
            return false;
        }
        
        try
        {
            int declaredLength = BitConverter.ToInt32(data, 0);
            return declaredLength == data.Length - 4;
        }
        catch
        {
            return false;
        }
    }
}