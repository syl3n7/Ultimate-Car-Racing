using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles the interpolation and movement of remote player vehicles.
/// This script should be attached to the remoteCarPrefab in GameManager.
/// </summary>
public class RemotePlayerController : MonoBehaviour
{
    [Header("Interpolation Settings")]
    public int positionHistorySize = 5;           // Number of position updates to store for interpolation
    public float interpolationSpeed = 10f;        // Speed of interpolation between positions
    public float rotationInterpolationSpeed = 8f; // Speed of rotation interpolation 
    public float velocityInterpolationSpeed = 5f; // Speed of velocity interpolation
    
    [Header("Debug Settings")]
    public bool showDebugInfo = false;
    
    // State information
    private string playerId;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetVelocity;
    private Vector3 targetAngularVelocity;
    
    // Position history for smooth interpolation
    private readonly Queue<PositionData> positionHistory = new Queue<PositionData>();
    
    // Last time we received a position update
    private float lastUpdateTime;
    
    // Reference to the rigidbody
    private Rigidbody rb;
    
    // Wheel references for visual rotation
    private Transform[] wheelTransforms;
    private WheelCollider[] wheelColliders;
    
    // Struct to store position data with timestamps
    private struct PositionData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float timestamp;
    }
    
    void Awake()
    {
        // Get rigidbody component
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.Log($"Added Rigidbody to RemotePlayerController {gameObject.name}");
        }
        
        // Configure rigidbody for network replication
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = false;
        
        // Initialize position history with current position
        PositionData initialData = new PositionData
        {
            position = transform.position,
            rotation = transform.rotation,
            velocity = Vector3.zero,
            angularVelocity = Vector3.zero,
            timestamp = Time.time
        };
        
        for (int i = 0; i < positionHistorySize; i++)
        {
            positionHistory.Enqueue(initialData);
        }
        
        // Find wheel colliders and transforms
        FindWheels();
    }
    
    private void FindWheels()
    {
        // Try to find wheel colliders in children
        wheelColliders = GetComponentsInChildren<WheelCollider>();
        
        // Find matching wheel transforms (visual meshes)
        if (wheelColliders != null && wheelColliders.Length > 0)
        {
            wheelTransforms = new Transform[wheelColliders.Length];
            
            // For each wheel collider, try to find the visual mesh
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                // Try to find a renderer in the children of the wheel collider
                foreach (Transform child in wheelColliders[i].transform)
                {
                    if (child.GetComponent<MeshRenderer>() != null || child.GetComponent<SkinnedMeshRenderer>() != null)
                    {
                        wheelTransforms[i] = child;
                        break;
                    }
                }
                
                if (wheelTransforms[i] == null)
                {
                    Debug.LogWarning($"Could not find visual mesh for wheel collider {wheelColliders[i].name}");
                }
            }
        }
    }
    
    void FixedUpdate()
    {
        if (positionHistory.Count == 0) return;
        
        // Calculate smoothed position from position history
        CalculateInterpolatedPosition();
        
        // Update wheel visuals
        UpdateWheelVisuals();
    }
    
    private void CalculateInterpolatedPosition()
    {
        // Convert history to array for easier manipulation
        PositionData[] historyArray = positionHistory.ToArray();
        
        // Use weighted average of recent positions, with more weight on newer positions
        Vector3 smoothedPosition = Vector3.zero;
        Quaternion smoothedRotation = Quaternion.identity;
        Vector3 smoothedVelocity = Vector3.zero;
        Vector3 smoothedAngularVelocity = Vector3.zero;
        float totalWeight = 0f;
        
        for (int i = 0; i < historyArray.Length; i++)
        {
            // More recent positions have higher weight
            float weight = (i + 1f) / historyArray.Length;
            
            smoothedPosition += historyArray[i].position * weight;
            smoothedVelocity += historyArray[i].velocity * weight;
            smoothedAngularVelocity += historyArray[i].angularVelocity * weight;
            totalWeight += weight;
        }
        
        // Normalize by total weight
        if (totalWeight > 0)
        {
            smoothedPosition /= totalWeight;
            smoothedVelocity /= totalWeight;
            smoothedAngularVelocity /= totalWeight;
        }
        
        // Rotation needs special handling - use the most recent rotation as a base
        smoothedRotation = historyArray[historyArray.Length - 1].rotation;
        
        // Apply interpolated values
        ApplyInterpolation(smoothedPosition, smoothedRotation, smoothedVelocity, smoothedAngularVelocity);
    }
    
    private void ApplyInterpolation(Vector3 smoothedPosition, Quaternion smoothedRotation, 
                                   Vector3 smoothedVelocity, Vector3 smoothedAngularVelocity)
    {
        // Smoothly move toward the target position and rotation
        if (rb != null)
        {
            // Position interpolation
            rb.position = Vector3.Lerp(rb.position, smoothedPosition, interpolationSpeed * Time.fixedDeltaTime);
            
            // Rotation interpolation
            rb.rotation = Quaternion.Slerp(rb.rotation, smoothedRotation, rotationInterpolationSpeed * Time.fixedDeltaTime);
            
            // Velocity interpolation
            rb.velocity = Vector3.Lerp(rb.velocity, smoothedVelocity, velocityInterpolationSpeed * Time.fixedDeltaTime);
            
            // Angular velocity interpolation 
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, smoothedAngularVelocity, velocityInterpolationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            // Fallback if no rigidbody
            transform.position = Vector3.Lerp(transform.position, smoothedPosition, interpolationSpeed * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, smoothedRotation, rotationInterpolationSpeed * Time.fixedDeltaTime);
        }
    }
    
    /// <summary>
    /// Update the remote player's position based on network data
    /// </summary>
    public void UpdatePosition(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        // Add to position history
        PositionData newData = new PositionData
        {
            position = position,
            rotation = rotation,
            velocity = velocity,
            angularVelocity = angularVelocity,
            timestamp = Time.time
        };
        
        // Add to history and maintain size limit
        positionHistory.Enqueue(newData);
        if (positionHistory.Count > positionHistorySize)
        {
            positionHistory.Dequeue();
        }
        
        // Store the receipt time
        lastUpdateTime = Time.time;
    }
    
    private void UpdateWheelVisuals()
    {
        // Update wheel visuals if we have wheel colliders
        if (wheelColliders == null || wheelTransforms == null) return;
        
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (wheelTransforms[i] != null)
            {
                // Get wheel position and rotation from collider
                wheelColliders[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
                
                // Apply to visual wheel
                wheelTransforms[i].position = pos;
                wheelTransforms[i].rotation = rot;
            }
        }
    }
    
    public void SetPlayerId(string id)
    {
        playerId = id;
        gameObject.name = $"RemotePlayer_{id}";
    }
    
    // For debugging
    void OnGUI()
    {
        if (showDebugInfo)
        {
            GUILayout.BeginArea(new Rect(10, 40, 300, 300));
            GUILayout.Label($"Player ID: {playerId}");
            GUILayout.Label($"Position: {transform.position}");
            GUILayout.Label($"Last update: {Time.time - lastUpdateTime:F2}s ago");
            GUILayout.Label($"History size: {positionHistory.Count}");
            GUILayout.EndArea();
        }
    }
}