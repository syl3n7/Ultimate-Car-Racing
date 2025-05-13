using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class RoomListItem : MonoBehaviour
{
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI playerCountText;
    public Button joinButton;
    
    private string roomId;
    private string roomName;
    
    public event Action<string, string> OnSelected;
    
    public void Initialize(string id, string name, int playerCount, int maxPlayers)
    {
        roomId = id;
        roomName = name;
        
        roomNameText.text = name;
        playerCountText.text = $"{playerCount}/{maxPlayers}";
        
        joinButton.onClick.AddListener(OnRoomClicked);
    }
    
    private void OnRoomClicked()
    {
        OnSelected?.Invoke(roomId, roomName);
    }
    
    private void OnDestroy()
    {
        joinButton.onClick.RemoveListener(OnRoomClicked);
    }
}