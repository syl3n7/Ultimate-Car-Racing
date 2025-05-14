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
        this.roomId = roomId;
        this.roomName = roomName;
        
        roomNameText.text = roomName;
        playerCountText.text = $"{playerCount}/{maxPlayers} Players";
        
        // Add click event listener
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnClick);
        }
    }
    
    private void OnClick()
    {
        OnSelected?.Invoke(roomId, roomName);
        
        // Highlight this item
        Button button = GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = colors.selectedColor;
        button.colors = colors;
    }
}