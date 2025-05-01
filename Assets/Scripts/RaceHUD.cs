using UnityEngine;
using TMPro;

public class RaceHUD : MonoBehaviour
{
    [Header("Player Info")]
    public TextMeshProUGUI positionText;
    public TextMeshProUGUI lapText;
    public TextMeshProUGUI speedText;
    
    [Header("Technical Info")]
    public TextMeshProUGUI latencyText;
    public TextMeshProUGUI fpsText;
    
    [Header("Display Settings")]
    public bool showFPS = true;
    public float updateInterval = 0.5f;
    
    private float nextUpdateTime;
    private int frameCount;
    private float timeElapsed;
    private float currentFps;
    
    void Update()
    {
        // FPS calculation
        if (showFPS)
        {
            frameCount++;
            timeElapsed += Time.unscaledDeltaTime;
            
            if (timeElapsed > 0.5f)
            {
                currentFps = frameCount / timeElapsed;
                frameCount = 0;
                timeElapsed = 0;
                
                if (fpsText != null)
                {
                    fpsText.text = $"FPS: {Mathf.Round(currentFps)}";
                }
            }
        }
        
        // Regular UI updates
        if (Time.time > nextUpdateTime)
        {
            UpdateRaceDisplay();
            nextUpdateTime = Time.time + updateInterval;
        }
    }
    
    private void UpdateRaceDisplay()
    {
        PlayerController localPlayer = GetLocalPlayer();
        if (localPlayer == null) return;
        
        // Update speed display
        if (speedText != null)
        {
            float speedKmh = localPlayer.Rigidbody.linearVelocity.magnitude * 3.6f; // Convert m/s to km/h
            speedText.text = $"{Mathf.Round(speedKmh)} km/h";
        }
        
        // Get race position from RaceManager
        if (positionText != null && RaceManager.Instance != null)
        {
            int position = RaceManager.Instance.GetPlayerPosition(localPlayer.PlayerId);
            positionText.text = GetPositionString(position);
        }
        
        // Update lap info
        if (lapText != null && RaceManager.Instance != null)
        {
            int currentLap = RaceManager.Instance.GetPlayerLap(localPlayer.PlayerId);
            int totalLaps = RaceManager.Instance.totalLaps;
            lapText.text = $"Lap {currentLap}/{totalLaps}";
        }
    }
    
    private string GetPositionString(int position)
    {
        string suffix;
        switch (position)
        {
            case 1: suffix = "st"; break;
            case 2: suffix = "nd"; break;
            case 3: suffix = "rd"; break;
            default: suffix = "th"; break;
        }
        return $"{position}{suffix}";
    }
    
    private PlayerController GetLocalPlayer()
    {
        if (GameManager.Instance == null) return null;
        
        foreach (PlayerController player in FindObjectsOfType<PlayerController>())
        {
            if (player.IsLocal)
            {
                return player;
            }
        }
        
        return null;
    }
}
