using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class RemotePlayerController : MonoBehaviour
{
    [Header("Interpolation Settings")]
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 8f;
    public float velocityLerpSpeed = 5f;
    
    [Header("Optimization")]
    public float updateDistanceThreshold = 0.05f; // Minimum distance to apply update
    public float maxInterpolationTime = 1f; // Maximum time to interpolate
    
    private string playerId;
    private Rigidbody rb;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetVelocity;
    private Vector3 targetAngularVelocity;
    private float lastUpdateTime;
    private bool hasInitialPosition = false;
    
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        lastUpdateTime = Time.time;
    }
    
    public void SetPlayerId(string id)
    {
        playerId = id;
        
        // Also tag this object appropriately
        gameObject.tag = "RemotePlayer";
        
        // Ensure this object won't interfere with camera following the local player
        foreach (Transform child in transform)
        {
            if (child.GetComponent<Camera>() != null)
            {
                child.gameObject.SetActive(false);
            }
        }
    }
    
    public void UpdatePosition(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        // For the first update, just teleport to avoid large jumps
        if (!hasInitialPosition)
        {
            transform.position = position;
            transform.rotation = rotation;
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;
            
            targetPosition = position;
            targetRotation = rotation;
            targetVelocity = velocity;
            targetAngularVelocity = angularVelocity;
            
            hasInitialPosition = true;
            return;
        }
        
        // Only update if the position has changed significantly
        float distanceChange = Vector3.Distance(position, targetPosition);
        if (distanceChange > updateDistanceThreshold)
        {
            targetPosition = position;
            targetRotation = rotation;
            targetVelocity = velocity;
            targetAngularVelocity = angularVelocity;
            lastUpdateTime = Time.time;
        }
    }
    
    void FixedUpdate()
    {
        // Skip interpolation if we don't have an initial position yet
        if (!hasInitialPosition)
            return;
        
        // Calculate interpolation factor based on time since last update
        float timeSinceUpdate = Time.time - lastUpdateTime;
        
        // If we haven't received an update in a while, slow down interpolation
        float timeScaleFactor = Mathf.Clamp01(1f - (timeSinceUpdate / maxInterpolationTime));
        
        // Apply smooth interpolation to position and rotation
        transform.position = Vector3.Lerp(transform.position, targetPosition, 
            positionLerpSpeed * timeScaleFactor * Time.fixedDeltaTime);
            
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 
            rotationLerpSpeed * timeScaleFactor * Time.fixedDeltaTime);
        
        // Apply smooth interpolation to velocities
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, 
            velocityLerpSpeed * timeScaleFactor * Time.fixedDeltaTime);
            
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, targetAngularVelocity, 
            velocityLerpSpeed * timeScaleFactor * Time.fixedDeltaTime);
    }
}