using UnityEngine;
using TMPro;
using UltimateCarRacing.Networking;

public class NetworkLatencyDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI latencyText;
    
    [Header("Display Settings")]
    public bool showMs = true;
    public bool colorCode = true;
    public Color goodLatencyColor = new Color(0.0f, 0.8f, 0.0f);
    public Color mediumLatencyColor = new Color(0.9f, 0.9f, 0.0f);
    public Color badLatencyColor = new Color(1.0f, 0.0f, 0.0f);
    public float updateInterval = 0.5f;
    
    private float nextUpdateTime;
    
    void Update()
    {
        if (Time.time > nextUpdateTime && NetworkManager.Instance != null)
        {
            UpdateLatencyDisplay();
            nextUpdateTime = Time.time + updateInterval;
        }
    }
    
    private void UpdateLatencyDisplay()
    {
        if (latencyText == null || NetworkManager.Instance == null)
            return;
            
        float latency = NetworkManager.Instance.AverageLatency;
        
        // Format latency display
        string latencyDisplay;
        if (showMs)
        {
            int latencyMs = Mathf.RoundToInt(latency * 1000);
            latencyDisplay = $" Ping: {latencyMs} ms";
        }
        else
        {
            latencyDisplay = $"Ping: {latency:F2} s";
        }
        
        // Update text
        latencyText.text = latencyDisplay;
        
        // Color-code based on latency quality
        if (colorCode)
        {
            if (latency < 0.075f) // < 75ms
            {
                latencyText.color = goodLatencyColor;
            }
            else if (latency < 0.150f) // < 150ms
            {
                latencyText.color = mediumLatencyColor;
            }
            else // >= 150ms
            {
                latencyText.color = badLatencyColor;
            }
        }
    }
}
