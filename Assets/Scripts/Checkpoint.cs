using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Tooltip("The index of this checkpoint in the race sequence")]
    public int checkpointIndex;
    
    private void OnTriggerEnter(Collider other)
    {
        // Check if a player car entered the checkpoint
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && player.IsLocal)
        {
            // Notify RaceManager about checkpoint
            if (RaceManager.Instance != null)
            {
                RaceManager.Instance.PassCheckpoint(player.PlayerId, checkpointIndex);
            }
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
