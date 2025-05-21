using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CarUIController : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private CarController carController;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private TextMeshProUGUI rpmText;
    [SerializeField] private TextMeshProUGUI gearText;

    [Header("Display Options")]
    [SerializeField] private bool showKMH = true; // If false, show MPH
    [SerializeField] private bool showGear = true;
    [SerializeField] private string speedFormat = "0";
    [SerializeField] private string rpmFormat = "0";

    private void Start()
    {
        // Try to find car controller if not assigned
        if (carController == null)
        {
            carController = FindObjectOfType<CarController>();
            if (carController == null)
            {
                Debug.LogWarning("CarController not found! UI will not update.");
                enabled = false;
                return;
            }
        }

        // Make sure we have the UI elements assigned
        if (speedText == null || rpmText == null)
        {
            Debug.LogWarning("UI Text elements not assigned. Speed/RPM will not be displayed.");
        }
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (carController == null) return;

        // Update speed display
        if (speedText != null)
        {
            float speedValue = carController.speedKmh;
            if (!showKMH)
            {
                speedValue = speedValue * 0.6213712f; // Convert to MPH
            }
            speedText.text = speedValue.ToString(speedFormat) + (showKMH ? " km/h" : " mph");
        }

        // Update RPM display
        if (rpmText != null)
        {
            rpmText.text = carController.engineRPM.ToString(rpmFormat) + " RPM";
        }

        // Update gear display if enabled
        if (gearText != null && showGear)
        {
            string gearDisplay;
            if (carController.currentGear > 0)
            {
                gearDisplay = carController.currentGear.ToString();
            }
            else if (carController.currentGear == 0)
            {
                gearDisplay = "N";
            }
            else
            {
                gearDisplay = "R";
            }
            gearText.text = gearDisplay;
        }
    }
}