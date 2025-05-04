using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UltimateCarRacing.Networking;
using Newtonsoft.Json;

#if UNITY_EDITOR
namespace UltimateCarRacing.Development
{
    public class ServerStressTest : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int minBots = 5;
        [SerializeField] private int maxBots = 50;
        [SerializeField] private int defaultBots = 10;
        [SerializeField] private float messageInterval = 0.1f;
        [SerializeField] private int messageSize = 256; // bytes
        
        [Header("UI Elements")]
        [SerializeField] private GameObject stressTestPanel;
        [SerializeField] private Slider botsSlider;
        [SerializeField] private TextMeshProUGUI botsCountText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI metricsText;
        
        private bool isStressTestRunning = false;
        private List<string> botClientIds = new List<string>();
        private int messagesSent = 0;
        private int messagesReceived = 0;
        private float startTime = 0;
        private Coroutine stressTestCoroutine;
        private string generatedData;

        private void Awake()
        {
            // Only show in development builds or editor
            #if !DEVELOPMENT_BUILD && !UNITY_EDITOR
            Destroy(gameObject);
            return;
            #endif
            
            // Initialize UI
            botsSlider.minValue = minBots;
            botsSlider.maxValue = maxBots;
            botsSlider.value = defaultBots;
            UpdateBotsCountText(defaultBots);
            
            // Hide panel by default
            stressTestPanel.SetActive(false);
            
            // Register button events
            botsSlider.onValueChanged.AddListener(UpdateBotsCountText);
            startButton.onClick.AddListener(StartStressTest);
            stopButton.onClick.AddListener(StopStressTest);
            
            // Generate random data once for message sending
            GenerateRandomMessageData();
        }

        private void OnEnable()
        {
            // Register network events
            NetworkManager.Instance.OnMessageReceived += HandleMessageReceived;
        }

        private void OnDisable()
        {
            // Unregister network events
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMessageReceived -= HandleMessageReceived;
        }

        private void Update()
        {
            // Toggle panel visibility with a key (Ctrl+F12)
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F12))
            {
                TogglePanel();
            }
            
            // Update metrics if test is running
            if (isStressTestRunning)
            {
                UpdateMetrics();
            }
        }

        private void TogglePanel()
        {
            stressTestPanel.SetActive(!stressTestPanel.activeSelf);
        }

        private void UpdateBotsCountText(float value)
        {
            int botCount = Mathf.RoundToInt(value);
            botsCountText.text = $"Bot Count: {botCount}";
        }

        private void StartStressTest()
        {
            if (isStressTestRunning || !NetworkManager.Instance.IsConnected)
                return;
                
            int botCount = Mathf.RoundToInt(botsSlider.value);
            statusText.text = "Status: Starting stress test...";
            
            // Reset metrics
            messagesSent = 0;
            messagesReceived = 0;
            startTime = Time.time;
            
            // Start the stress test
            stressTestCoroutine = StartCoroutine(RunStressTest(botCount));
        }

        private void StopStressTest()
        {
            if (!isStressTestRunning)
                return;
                
            statusText.text = "Status: Stopping stress test...";
            
            if (stressTestCoroutine != null)
                StopCoroutine(stressTestCoroutine);
                
            // Clean up bots
            StartCoroutine(CleanupBots());
        }

        private IEnumerator RunStressTest(int botCount)
        {
            isStressTestRunning = true;
            
            // Tell the server we're starting a stress test
            Dictionary<string, object> startMessage = new Dictionary<string, object>
            {
                { "type", "STRESS_TEST_START" },
                { "bot_count", botCount }
            };
            
            NetworkManager.Instance.SendTcpMessage(startMessage);
            
            // Wait for bot creation confirmation
            yield return new WaitForSeconds(1.0f);
            
            // Start sending messages
            while (isStressTestRunning)
            {
                // Send messages from each bot to all other bots
                foreach (string botId in botClientIds)
                {
                    // Create a message with random data to simulate payload
                    Dictionary<string, object> message = new Dictionary<string, object>
                    {
                        { "type", "STRESS_TEST_MESSAGE" },
                        { "from_bot", botId },
                        { "data", generatedData }
                    };
                    
                    // Send to room (will reach all bots)
                    if (NetworkManager.Instance.CurrentRoomId != null)
                    {
                        message["room_id"] = NetworkManager.Instance.CurrentRoomId;
                        NetworkManager.Instance.SendMessageToRoom(JsonConvert.SerializeObject(message));
                        messagesSent++;
                    }
                }
                
                yield return new WaitForSeconds(messageInterval);
            }
        }

        private IEnumerator CleanupBots()
        {
            // Tell server to clean up bots
            Dictionary<string, object> stopMessage = new Dictionary<string, object>
            {
                { "type", "STRESS_TEST_STOP" }
            };
            
            NetworkManager.Instance.SendTcpMessage(stopMessage);
            
            // Wait for confirmation
            yield return new WaitForSeconds(1.0f);
            
            botClientIds.Clear();
            isStressTestRunning = false;
            statusText.text = "Status: Stress test stopped";
        }

        private void HandleMessageReceived(string fromClient, string message)
        {
            try
            {
                Dictionary<string, object> data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                
                if (data != null && data.TryGetValue("type", out object typeObj))
                {
                    string type = typeObj.ToString();
                    
                    if (type == "STRESS_TEST_BOTS_CREATED")
                    {
                        // Get the bot client IDs
                        Newtonsoft.Json.Linq.JArray botsArray = data["bot_ids"] as Newtonsoft.Json.Linq.JArray;
                        botClientIds.Clear();
                        
                        foreach (var botId in botsArray)
                        {
                            botClientIds.Add(botId.ToString());
                        }
                        
                        statusText.text = $"Status: Running with {botClientIds.Count} bots";
                    }
                    else if (type == "STRESS_TEST_MESSAGE" && isStressTestRunning)
                    {
                        // Count received messages
                        messagesReceived++;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing stress test message: {e.Message}");
            }
        }

        private void UpdateMetrics()
        {
            float elapsedTime = Time.time - startTime;
            
            if (elapsedTime > 0)
            {
                float messagesPerSecond = messagesSent / elapsedTime;
                float receivedPerSecond = messagesReceived / elapsedTime;
                
                metricsText.text = $"Messages Sent: {messagesSent}\n" +
                                  $"Messages Received: {messagesReceived}\n" +
                                  $"Test Duration: {elapsedTime:F1}s\n" +
                                  $"Send Rate: {messagesPerSecond:F1}/s\n" +
                                  $"Receive Rate: {receivedPerSecond:F1}/s\n" +
                                  $"Message Size: {messageSize} bytes\n" +
                                  $"Network Usage: {((messagesSent + messagesReceived) * messageSize / 1024f / elapsedTime):F2} KB/s";
            }
        }

        private void GenerateRandomMessageData()
        {
            // Generate random data of specified size
            System.Random random = new System.Random();
            char[] data = new char[messageSize];
            
            for (int i = 0; i < messageSize; i++)
            {
                // Use printable ASCII characters
                data[i] = (char)random.Next(32, 127);
            }
            
            generatedData = new string(data);
        }
    }
}
#endif