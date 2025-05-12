using UnityEngine;
using UnityEngine.SceneManagement;

public class GameBootstrap : MonoBehaviour
{
    public GameObject gameManagerPrefab;
    
    void Start()
    {
        // Create the GameManager if it doesn't exist
        if (GameManager.Instance == null && gameManagerPrefab != null)
        {
            Instantiate(gameManagerPrefab);
        }
        
        // Spawn the local player
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SpawnLocalPlayer();
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
