using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.IO;

public class TrackSelectionDropdown : MonoBehaviour
{
    public TMP_Dropdown trackDropdown;

    void Start()
    {
        if (trackDropdown == null)
        {
            Debug.LogError("TrackDropdown is not assigned.");
            return;
        }

        // Retrieve track scenes from build settings (elements with build indices 1 to 3)
        List<string> options = new List<string>();
        int firstIndex = 1;
        int lastIndex = Mathf.Min(3, SceneManager.sceneCountInBuildSettings - 1);

        for (int i = firstIndex; i <= lastIndex; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            options.Add(sceneName);
        }

        if (options.Count == 0)
        {
            Debug.LogError("No track scenes found in build settings in indices 1 to 3!");
            return;
        }
        
        trackDropdown.ClearOptions();
        trackDropdown.AddOptions(options);
        trackDropdown.onValueChanged.AddListener(OnTrackSelected);
    }

    void OnTrackSelected(int index)
    {
        // Since we're using build indices 1 to 3, add 1 to the dropdown index.
        GameManager.SelectedTrackIndex = index + 1;
        Debug.Log("Selected track build index: " + (index + 1));
    }
}