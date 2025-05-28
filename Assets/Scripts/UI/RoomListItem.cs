using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomListItem : MonoBehaviour
{
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI playerCountText;
    
    public event Action<string, string> OnSelected;
    
    private string roomId;
    private string roomName;
    
    public void Initialize(string roomId, string roomName, int playerCount, int maxPlayers)
    {
        Debug.Log($"Initializing room list item: {roomName} (ID: {roomId})");
        
        this.roomId = roomId;
        this.roomName = roomName;
        
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
        
        // Set the text values if components exist
        if (roomNameText != null)
            roomNameText.text = roomName;
            
        if (playerCountText != null)
            playerCountText.text = $"{playerCount}/{maxPlayers} Players";
        
        // Add click event listener
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            Debug.Log($"Added click listener to room: {roomName}");
        }
        else
        {
            Debug.LogError("Button component missing from RoomListItem!");
        }
    }
    
    private void OnClick()
    {
        Debug.Log($"Room selected: {roomName} (ID: {roomId})");
        OnSelected?.Invoke(roomId, roomName);
        
        // Highlight this item
        Button button = GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = colors.selectedColor;
        button.colors = colors;
    }
}