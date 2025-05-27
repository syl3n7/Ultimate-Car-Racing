using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor script to automatically setup the Console Command System UI
/// Run this from the Tools menu after adding ConsoleCommandSystem to a GameObject
/// </summary>
public class ConsoleSetup : Editor
{
    [MenuItem("Tools/Setup Console Command System")]
    public static void SetupConsole()
    {
        // Find or create Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create Console Panel
        GameObject consolePanel = new GameObject("ConsolePanel");
        consolePanel.transform.SetParent(canvas.transform, false);
        
        RectTransform consolePanelRect = consolePanel.AddComponent<RectTransform>();
        consolePanelRect.anchorMin = new Vector2(0, 0.5f);
        consolePanelRect.anchorMax = new Vector2(1, 1);
        consolePanelRect.offsetMin = new Vector2(10, 10);
        consolePanelRect.offsetMax = new Vector2(-10, -10);
        
        Image consolePanelBg = consolePanel.AddComponent<Image>();
        consolePanelBg.color = new Color(0, 0, 0, 0.8f);
        
        // Create Output Text
        GameObject outputTextObj = new GameObject("OutputText");
        outputTextObj.transform.SetParent(consolePanel.transform, false);
        
        RectTransform outputRect = outputTextObj.AddComponent<RectTransform>();
        outputRect.anchorMin = new Vector2(0, 0.15f);
        outputRect.anchorMax = new Vector2(1, 1);
        outputRect.offsetMin = new Vector2(10, 10);
        outputRect.offsetMax = new Vector2(-10, -10);
        
        Text outputText = outputTextObj.AddComponent<Text>();
        outputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        outputText.fontSize = 14;
        outputText.color = Color.white;
        outputText.alignment = TextAnchor.LowerLeft;
        outputText.verticalOverflow = VerticalWrapMode.Overflow;
        outputText.text = "Console ready...";
        
        // Create Input Field
        GameObject inputFieldObj = new GameObject("InputField");
        inputFieldObj.transform.SetParent(consolePanel.transform, false);
        
        RectTransform inputRect = inputFieldObj.AddComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0, 0);
        inputRect.anchorMax = new Vector2(1, 0.15f);
        inputRect.offsetMin = new Vector2(10, 10);
        inputRect.offsetMax = new Vector2(-10, -5);
        
        Image inputBg = inputFieldObj.AddComponent<Image>();
        inputBg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        
        InputField inputField = inputFieldObj.AddComponent<InputField>();
        
        // Create input text
        GameObject inputTextObj = new GameObject("Text");
        inputTextObj.transform.SetParent(inputFieldObj.transform, false);
        
        RectTransform inputTextRect = inputTextObj.AddComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = new Vector2(10, 0);
        inputTextRect.offsetMax = new Vector2(-10, 0);
        
        Text inputText = inputTextObj.AddComponent<Text>();
        inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        inputText.fontSize = 14;
        inputText.color = Color.white;
        inputText.supportRichText = false;
        
        inputField.textComponent = inputText;
        
        // Create placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(inputFieldObj.transform, false);
        
        RectTransform placeholderRect = placeholderObj.AddComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10, 0);
        placeholderRect.offsetMax = new Vector2(-10, 0);
        
        Text placeholderText = placeholderObj.AddComponent<Text>();
        placeholderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        placeholderText.fontSize = 14;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        placeholderText.text = "Type command here (e.g., connect(192.168.1.1))...";
        
        inputField.placeholder = placeholderText;
        
        // Create Toggle Button
        GameObject toggleButtonObj = new GameObject("ToggleButton");
        toggleButtonObj.transform.SetParent(canvas.transform, false);
        
        RectTransform toggleRect = toggleButtonObj.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0, 1);
        toggleRect.anchorMax = new Vector2(0, 1);
        toggleRect.pivot = new Vector2(0, 1);
        toggleRect.anchoredPosition = new Vector2(10, -10);
        toggleRect.sizeDelta = new Vector2(100, 30);
        
        Image toggleBg = toggleButtonObj.AddComponent<Image>();
        toggleBg.color = new Color(0.2f, 0.4f, 0.8f, 0.8f);
        
        Button toggleButton = toggleButtonObj.AddComponent<Button>();
        
        GameObject toggleTextObj = new GameObject("Text");
        toggleTextObj.transform.SetParent(toggleButtonObj.transform, false);
        
        RectTransform toggleTextRect = toggleTextObj.AddComponent<RectTransform>();
        toggleTextRect.anchorMin = Vector2.zero;
        toggleTextRect.anchorMax = Vector2.one;
        toggleTextRect.offsetMin = Vector2.zero;
        toggleTextRect.offsetMax = Vector2.zero;
        
        Text toggleText = toggleTextObj.AddComponent<Text>();
        toggleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        toggleText.fontSize = 12;
        toggleText.color = Color.white;
        toggleText.alignment = TextAnchor.MiddleCenter;
        toggleText.text = "Console";
        
        // Find or create ConsoleCommandSystem component
        ConsoleCommandSystem consoleSystem = FindObjectOfType<ConsoleCommandSystem>();
        if (consoleSystem == null)
        {
            GameObject consoleManagerObj = new GameObject("ConsoleManager");
            consoleSystem = consoleManagerObj.AddComponent<ConsoleCommandSystem>();
        }
        
        // Assign references
        consoleSystem.consolePanel = consolePanel;
        consoleSystem.commandInput = inputField;
        consoleSystem.outputText = outputText;
        consoleSystem.toggleButton = toggleButton;
        
        // Hide console initially
        consolePanel.SetActive(false);
        
        Debug.Log("Console Command System setup complete!");
        Debug.Log("Press ` (backtick) key or click the Console button to toggle the console.");
        Debug.Log("Type 'help' for available commands.");
        
        // Select the console system in hierarchy
        Selection.activeGameObject = consoleSystem.gameObject;
    }
}
#endif
