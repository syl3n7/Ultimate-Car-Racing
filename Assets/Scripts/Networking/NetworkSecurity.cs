using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;

namespace CarRacing.Networking
{
    /// <summary>
    /// Handles security functions for network communications including UDP encryption
    /// as specified in the SERVER-README.md documentation (sections 4.2 and 13.2)
    /// </summary>
    public class NetworkSecurity
    {
        // Encryption constants
        private const int AES_KEY_SIZE = 256;  // AES-256 as required by docs
        private const int AES_BLOCK_SIZE = 128;  // AES block size in bits
        private const int IV_SIZE = 16;  // 128 bits = 16 bytes
        
        // Cryptographic objects
        private Aes aesAlgorithm;
        private byte[] encryptionKey;
        private bool isEncryptionEnabled = false;
        
        // Encryption configuration
        public bool IsEncryptionEnabled => isEncryptionEnabled;
        
        /// <summary>
        /// Initialize the security system with default settings
        /// </summary>
        public NetworkSecurity()
        {
            // Create AES provider
            aesAlgorithm = Aes.Create();
            aesAlgorithm.KeySize = AES_KEY_SIZE;
            aesAlgorithm.BlockSize = AES_BLOCK_SIZE;
            aesAlgorithm.Mode = CipherMode.CBC;  // CBC mode as required by docs
            aesAlgorithm.Padding = PaddingMode.PKCS7;
        }
        
        /// <summary>
        /// Set up encryption using the session ID as a seed for key generation
        /// </summary>
        /// <param name="sessionId">The unique session ID for this client</param>
        public void SetupEncryption(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("Cannot set up encryption with empty session ID");
                isEncryptionEnabled = false;
                return;
            }
            
            try
            {
                // Generate encryption key based on session ID
                // In a real implementation, you would get this key from the server
                // via secure channel, but for this example we derive it from the sessionId
                using (var keyDerivation = new Rfc2898DeriveBytes(
                    sessionId,
                    Encoding.UTF8.GetBytes("CarRacingUDP-Salt"),  // Fixed salt, should come from server
                    10000))  // 10000 iterations for key derivation
                {
                    // Generate a 256-bit (32-byte) key
                    encryptionKey = keyDerivation.GetBytes(32);
                    isEncryptionEnabled = true;
                    
                    Debug.Log("UDP encryption has been enabled");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error setting up encryption: {ex.Message}");
                isEncryptionEnabled = false;
            }
        }
        
        /// <summary>
        /// Create an encrypted UDP packet from game data
        /// </summary>
        /// <param name="data">The game data to encrypt and send</param>
        /// <returns>Encrypted packet bytes ready to send over UDP</returns>
        public byte[] CreatePacket(Dictionary<string, object> data)
        {
            try
            {
                // Convert data to JSON
                string json = JsonConvert.SerializeObject(data);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                
                // If encryption is not enabled, return plain text with a terminator
                if (!isEncryptionEnabled)
                {
                    byte[] packet = new byte[jsonBytes.Length + 1];
                    Buffer.BlockCopy(jsonBytes, 0, packet, 0, jsonBytes.Length);
                    packet[jsonBytes.Length] = (byte)'\n';  // Add line terminator
                    return packet;
                }
                
                // Generate a random IV for each packet (security best practice)
                aesAlgorithm.GenerateIV();
                byte[] iv = aesAlgorithm.IV;  // 16 bytes for AES
                
                // Set the encryption key
                aesAlgorithm.Key = encryptionKey;
                
                // Encrypt the JSON data
                byte[] encryptedData;
                using (ICryptoTransform encryptor = aesAlgorithm.CreateEncryptor())
                {
                    encryptedData = encryptor.TransformFinalBlock(jsonBytes, 0, jsonBytes.Length);
                }
                
                // Create the final packet: [IV (16 bytes)][Encrypted Data]
                byte[] packet = new byte[IV_SIZE + encryptedData.Length];
                Buffer.BlockCopy(iv, 0, packet, 0, IV_SIZE);
                Buffer.BlockCopy(encryptedData, 0, packet, IV_SIZE, encryptedData.Length);
                
                return packet;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating encrypted packet: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Decrypt a received UDP packet
        /// </summary>
        /// <param name="encryptedPacket">The encrypted packet received from the network</param>
        /// <returns>Decrypted game data or null if decryption fails</returns>
        public Dictionary<string, object> DecryptPacket(byte[] encryptedPacket)
        {
            try
            {
                // Check if the packet is large enough to contain an IV
                if (!isEncryptionEnabled || encryptedPacket.Length <= IV_SIZE)
                {
                    // Try to parse as plain text JSON
                    string json = Encoding.UTF8.GetString(encryptedPacket).TrimEnd('\n', '\0');
                    return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                }
                
                // Extract the IV from the beginning of the packet
                byte[] iv = new byte[IV_SIZE];
                Buffer.BlockCopy(encryptedPacket, 0, iv, 0, IV_SIZE);
                
                // Set up decryption parameters
                aesAlgorithm.Key = encryptionKey;
                aesAlgorithm.IV = iv;
                
                // Extract and decrypt the data portion
                byte[] encryptedData = new byte[encryptedPacket.Length - IV_SIZE];
                Buffer.BlockCopy(encryptedPacket, IV_SIZE, encryptedData, 0, encryptedData.Length);
                
                // Decrypt the data
                byte[] decryptedData;
                using (ICryptoTransform decryptor = aesAlgorithm.CreateDecryptor())
                {
                    decryptedData = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
                }
                
                // Convert from JSON to dictionary
                string json = Encoding.UTF8.GetString(decryptedData);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error decrypting packet: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Hash a password using SHA-256 for secure authentication
        /// </summary>
        /// <param name="password">The password to hash</param>
        /// <returns>Base64-encoded hash of the password</returns>
        public string HashPassword(string password)
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                    return string.Empty;
                
                using (SHA256 sha256 = SHA256.Create())
                {
                    // Compute hash
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                    
                    // Convert to Base64 string for transmission
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error hashing password: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Clean up cryptographic resources
        /// </summary>
        public void Cleanup()
        {
            try
            {
                isEncryptionEnabled = false;
                
                if (encryptionKey != null)
                {
                    Array.Clear(encryptionKey, 0, encryptionKey.Length);
                    encryptionKey = null;
                }
                
                aesAlgorithm?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during security cleanup: {ex.Message}");
            }
        }
    }
}