using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class CarSelectionDropdown : MonoBehaviour
{
    public TMP_Dropdown carDropdown;

    void Start()
    {
        if (carDropdown == null)
        {
            Debug.LogError("CarDropdown is not assigned.");
            return;
        }

        // Populate the dropdown with car names
        List<string> options = new List<string>() { "Car 1", "Car 2", "Car 3" };
        carDropdown.ClearOptions();
        carDropdown.AddOptions(options);
        carDropdown.onValueChanged.AddListener(OnCarSelected);
    }

    void OnCarSelected(int index)
    {
        GameManager.SelectedCarIndex = index;
        Debug.Log("Selected car index: " + index);
    }
}