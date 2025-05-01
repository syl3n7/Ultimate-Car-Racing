using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    public GameObject networkManagerPrefab;
    public GameObject sceneTransitionManagerPrefab;
    
    void Awake()
    {
        // Check if NetworkManager exists
        if (FindObjectOfType<UltimateCarRacing.Networking.NetworkManager>() == null && networkManagerPrefab != null)
        {
            Instantiate(networkManagerPrefab);
        }
        
        // Check if SceneTransitionManager exists
        if (FindObjectOfType<SceneTransitionManager>() == null && sceneTransitionManagerPrefab != null)
        {
            Instantiate(sceneTransitionManagerPrefab);
        }
    }
}