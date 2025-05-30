using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Test script to validate back button functionality in the UI
/// Add this to any GameObject in the scene to test back button navigation
/// </summary>
public class BackButtonTester : MonoBehaviour
{
    [Header("Test Controls")]
    [SerializeField] private KeyCode testBackToMain = KeyCode.F1;
    [SerializeField] private KeyCode testBackFromMultiplayer = KeyCode.F2;
    [SerializeField] private KeyCode testBackFromRoomList = KeyCode.F3;
    [SerializeField] private KeyCode validateButtonsKey = KeyCode.F4;

    private void Update()
    {
        if (UIManager.Instance == null) return;

        // Test back button functionality with keyboard shortcuts
        if (Input.GetKeyDown(testBackToMain))
        {
            Debug.Log("Testing BackToMain functionality...");
            UIManager.Instance.ShowMainMenu();
        }

        if (Input.GetKeyDown(testBackFromMultiplayer))
        {
            Debug.Log("Testing BackFromMultiplayer functionality...");
            if (UIManager.Instance != null)
            {
                // Use reflection to call BackFromMultiplayer method
                var method = typeof(UIManager).GetMethod("BackFromMultiplayer");
                if (method != null)
                {
                    method.Invoke(UIManager.Instance, null);
                }
                else
                {
                    Debug.LogError("BackFromMultiplayer method not found!");
                }
            }
        }

        if (Input.GetKeyDown(testBackFromRoomList))
        {
            Debug.Log("Testing BackFromRoomList functionality...");
            UIManager.Instance.ShowMultiplayerPanel();
        }

        if (Input.GetKeyDown(validateButtonsKey))
        {
            ValidateBackButtons();
        }
    }

    private void ValidateBackButtons()
    {
        Debug.Log("=== BACK BUTTON VALIDATION TEST ===");

        if (UIManager.Instance == null)
        {
            Debug.LogError("UIManager.Instance is null!");
            return;
        }

        // Use reflection to check button references
        var uiManagerType = typeof(UIManager);
        
        CheckButtonField(uiManagerType, "backToMainButton", "Profile Panel → Main Menu");
        CheckButtonField(uiManagerType, "backFromMultiplayerButton", "Multiplayer Panel → Main Menu");
        CheckButtonField(uiManagerType, "backFromRoomListButton", "Room List Panel → Multiplayer Panel");

        Debug.Log("=== END VALIDATION TEST ===");
        Debug.Log("Press F1-F3 to test back button functionality directly");
    }

    private void CheckButtonField(System.Type type, string fieldName, string purpose)
    {
        var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            var button = field.GetValue(UIManager.Instance) as Button;
            if (button != null)
            {
                Debug.Log($"✓ {fieldName}: OK - Connected to '{button.gameObject.name}' ({purpose})");
            }
            else
            {
                Debug.LogError($"✗ {fieldName}: NULL - {purpose} navigation broken!");
            }
        }
        else
        {
            Debug.LogError($"✗ {fieldName}: Field not found!");
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("Back Button Tester", GUI.skin.box);
        GUILayout.Label($"F1: Test Back to Main");
        GUILayout.Label($"F2: Test Back from Multiplayer");
        GUILayout.Label($"F3: Test Back from Room List");
        GUILayout.Label($"F4: Validate Button References");
        
        if (GUILayout.Button("Validate Now"))
        {
            ValidateBackButtons();
        }
        
        GUILayout.EndArea();
    }
}
