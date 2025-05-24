using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;

public class RoomListItem : MonoBehaviour
{
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI playerCountText;
    
    public event Action<string, string> OnSelected;
    
    private string roomId;
    private string roomName;
    
    // Maximum length for displayed room name (to prevent UI overflow attacks)
    private const int MAX_DISPLAYED_NAME_LENGTH = 20;
    
    // Regular expression to validate room IDs (alphanumeric and hyphens only)
    private static readonly Regex ValidRoomIdRegex = new Regex(@"^[a-zA-Z0-9\-]+$");
    
    public void Initialize(string roomId, string roomName, int playerCount, int maxPlayers)
    {
        // Validate inputs to prevent potential injection or overflow attacks
        if (string.IsNullOrEmpty(roomId) || !ValidRoomIdRegex.IsMatch(roomId))
        {
            Debug.LogWarning($"Invalid room ID format received: {roomId}");
            return;
        }
        
        if (string.IsNullOrEmpty(roomName))
        {
            roomName = "Unnamed Room";
        }
        
        // Sanitize room name by limiting length and removing potentially harmful characters
        string sanitizedRoomName = SanitizeRoomName(roomName);
        
        // Log without exposing full details in release builds
        #if DEBUG
        Debug.Log($"Initializing room list item: {sanitizedRoomName} (ID: {roomId})");
        #endif
        
        this.roomId = roomId;
        this.roomName = sanitizedRoomName;
        
        // Make sure text components exist
        if (roomNameText == null)
        {
            Debug.LogError("roomNameText is null on RoomListItem");
            roomNameText = transform.Find("RoomNameText").GetComponent<TextMeshProUGUI>();
            if (roomNameText == null)
            {
                Debug.LogError("Could not find RoomNameText child component!");
            }
        }
        
        if (playerCountText == null)
        {
            Debug.LogError("playerCountText is null on RoomListItem");
            playerCountText = transform.Find("PlayerCountText").GetComponent<TextMeshProUGUI>();
            if (playerCountText == null)
            {
                Debug.LogError("Could not find PlayerCountText child component!");
            }
        }
        
        // Validate player counts to prevent overflow or negative values
        playerCount = Mathf.Clamp(playerCount, 0, 999);
        maxPlayers = Mathf.Clamp(maxPlayers, 1, 999);
        
        // Set the text values if components exist
        if (roomNameText != null)
            roomNameText.text = sanitizedRoomName;
            
        if (playerCountText != null)
            playerCountText.text = $"{playerCount}/{maxPlayers} Players";
        
        // Add click event listener
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            #if DEBUG
            Debug.Log($"Added click listener to room: {sanitizedRoomName}");
            #endif
        }
        else
        {
            Debug.LogError("Button component missing from RoomListItem!");
        }
    }
    
    private void OnClick()
    {
        // Only expose minimal information in logs
        #if DEBUG
        Debug.Log($"Room selected: {roomName} (ID: {roomId})");
        #else
        Debug.Log("Room selected");
        #endif
        
        // Validate roomId one more time before invoking the event
        if (!string.IsNullOrEmpty(roomId) && ValidRoomIdRegex.IsMatch(roomId))
        {
            OnSelected?.Invoke(roomId, roomName);
            
            // Highlight this item
            Button button = GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = colors.selectedColor;
            button.colors = colors;
        }
        else
        {
            Debug.LogWarning("Attempted to select a room with invalid ID");
        }
    }
    
    // Sanitize room name to prevent potential security issues
    private string SanitizeRoomName(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "Unnamed Room";
            
        // Trim the name to prevent UI overflow
        string trimmed = input.Length > MAX_DISPLAYED_NAME_LENGTH ? 
            input.Substring(0, MAX_DISPLAYED_NAME_LENGTH) + "..." : input;
            
        // Remove any control characters that could cause issues
        return Regex.Replace(trimmed, @"[\u0000-\u001F]", "");
    }
}