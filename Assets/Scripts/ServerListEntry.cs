using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ServerListEntry : MonoBehaviour
{
    public TextMeshProUGUI serverNameText;
    public TextMeshProUGUI hostNameText; 
    public TextMeshProUGUI playerCountText;
    public Button joinButton;
    
    void Setup(string serverName, string hostName, string playerCount, Action onJoinClicked)
    {
        serverNameText.text = serverName;
        hostNameText.text = hostName;
        playerCountText.text = playerCount;
        
        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoinClicked?.Invoke());
    }
}