using UnityEngine;

public class CustomWheel : MonoBehaviour
{
    [Header("Suspension Settings")]
    public float suspensionDistance = 0.3f;
    public float springStrength = 30000f;
    public float damperStrength = 3000f;
    public LayerMask groundMask;
    
    [Header("Wheel Settings")]
    public Transform visualWheel;
    // FIXED: Set a proper default radius
    public float wheelRadius = 0.37f; // Proper size for car wheels
    
    // Add this to prevent accidental changes
    private bool hasSetRadius = false;
    
    private Rigidbody rb;
    private float lastCompression;
    private bool isGrounded = false;
    
    void Start()
    {
        rb = GetComponentInParent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("No Rigidbody found on parent! The CustomWheel must be a child of a GameObject with a Rigidbody.");
            enabled = false;
            return;
        }
        
        // FIXED: Don't override manually set values
        // Just validate that the wheel radius is reasonable
        if (wheelRadius > 1.0f)
        {
            Debug.LogError($"Wheel radius is too large: {wheelRadius}! Setting to 0.37f");
            wheelRadius = 0.37f;
        }
        else if (wheelRadius < 0.1f)
        {
            Debug.LogWarning($"Wheel radius is very small: {wheelRadius}! This might cause physics issues.");
        }
        
        // Remove this block that was overriding manual values
        /*
        if (visualWheel != null && !hasSetRadius)
        {
            float visualRadius = visualWheel.localScale.y * 0.5f;
            wheelRadius = Mathf.Min(visualRadius, 0.5f); // Cap at 0.5 units
            Debug.Log($"Setting wheel radius to {wheelRadius} based on visual wheel");
        }
        */
        
        // We've decided to keep the manual value, so mark it as set
        hasSetRadius = true;
        
        // Debug log to verify the ground layer mask is set correctly
        Debug.Log($"Wheel {name} using ground mask: {groundMask.value}, layer 3 is: {1 << 3}");
        
        // Make sure the groundMask is set correctly
        if (groundMask.value == 0)
        {
            Debug.LogWarning($"Ground mask on {name} is not set! Defaulting to layer 3 (Ground)");
            groundMask = 1 << 3; // Layer 3 is "Ground"
        }

        // If the visual wheel exists, try to position correctly relative to it
        if (visualWheel != null)
        {
            // Get the world position of the visual wheel
            Vector3 wheelPosition = visualWheel.position;
            
            // The CustomWheel object should be positioned at the axle height
            // (same Y as center of wheel)
            Vector3 correctPosition = transform.position;
            correctPosition.y = wheelPosition.y; // Match Y position with visual wheel
            
            // If the positions are significantly different, warn about it
            if (Mathf.Abs(transform.position.y - wheelPosition.y) > 0.5f)
            {
                Debug.LogWarning($"CustomWheel {name} is positioned far from its visual wheel " +
                               $"({transform.position.y} vs {wheelPosition.y}). This may cause physics issues.");
            }
        }

        // // Update the collider to match the wheel position
        // if (GetComponent<SphereCollider>() == null)
        // {
        //     SphereCollider collider = gameObject.AddComponent<SphereCollider>();
        //     collider.radius = 0.003f;
            
        //     // If there's a visual wheel, offset the collider to match its position
        //     if (visualWheel != null)
        //     {
        //         collider.center = transform.InverseTransformPoint(visualWheel.position);
        //     }
            
        //     collider.material = new PhysicsMaterial { 
        //         dynamicFriction = 0.6f,
        //         staticFriction = 0.6f,
        //         bounciness = 0.1f
        //     };
        // }
    }
    
    void FixedUpdate()
    {
        if (rb == null) return;
        if (visualWheel == null) return;
        
        // IMPORTANT: Use visualWheel position for raycasting if it exists
        Vector3 wheelWorldPos = visualWheel != null ? visualWheel.position : transform.position;
        RaycastHit hit;
        
        // Debug ray visualization
        Debug.DrawRay(wheelWorldPos, Vector3.down * (suspensionDistance + wheelRadius), Color.red);
        
        // Cast ray down from the visual wheel position
        if (Physics.Raycast(wheelWorldPos, Vector3.down, out hit, suspensionDistance + wheelRadius, groundMask))
        {
            isGrounded = true;
            
            // Calculate suspension compression (0 = fully extended, 1 = fully compressed)
            float compression = 1f - ((hit.distance - wheelRadius) / suspensionDistance);
            compression = Mathf.Clamp01(compression);
            
            // Calculate spring and damper forces
            float springForce = compression * springStrength;
            float damperForce = ((compression - lastCompression) / Time.fixedDeltaTime) * damperStrength;
            
            // IMPROVED: Make damping more responsive when extending (prevents bouncing)
            if (compression < lastCompression)
                damperForce *= 2.0f; // Double damping when suspension is extending
                
            float totalForce = springForce + damperForce;
            
            lastCompression = compression;
            
            // Apply counteracting gravity force based on car mass
            float gravityCompensation = rb.mass * Mathf.Abs(Physics.gravity.y) * 0.25f;
            totalForce -= gravityCompensation;
            
            // Only apply positive force
            if (totalForce > 0)
            {
                rb.AddForceAtPosition(Vector3.up * totalForce, wheelWorldPos);
            }
            
            // IMPROVED: Set minimum height for visual wheel to prevent clipping
            if (visualWheel != null)
            {
                // First, get the current local position
                Vector3 localPos = visualWheel.localPosition;
                
                // Create a zero vector - we'll modify the correct axis
                Vector3 suspensionOffset = Vector3.zero;
                
                // Figure out which local axis points downward in world space
                Vector3 worldDown = Vector3.down;
                Vector3 localDown = visualWheel.InverseTransformDirection(worldDown).normalized;
                
                // Find the dominant axis (X, Y, or Z) that corresponds to up/down
                float absX = Mathf.Abs(localDown.x);
                float absY = Mathf.Abs(localDown.y);
                float absZ = Mathf.Abs(localDown.z);
                
                // Apply suspension to the dominant axis
                if (absY > absX && absY > absZ)
                {
                    // Y axis is dominant (common case)
                    float direction = Mathf.Sign(localDown.y);
                    suspensionOffset.y = direction * -compression * suspensionDistance;
                }
                else if (absX > absY && absX > absZ)
                {
                    // X axis is dominant
                    float direction = Mathf.Sign(localDown.x);
                    suspensionOffset.x = direction * -compression * suspensionDistance;
                }
                else
                {
                    // Z axis is dominant
                    float direction = Mathf.Sign(localDown.z);
                    suspensionOffset.z = direction * -compression * suspensionDistance;
                }
                
                // Apply the offset
                visualWheel.localPosition = suspensionOffset;
                
                // Debug visualization
                Debug.DrawRay(visualWheel.position, visualWheel.TransformDirection(suspensionOffset).normalized, Color.blue);
            }
            
            Debug.DrawLine(wheelWorldPos, hit.point, Color.green);
        }
        else
        {
            isGrounded = false;
            lastCompression = 0f;
            
            // IMPROVED: Reset wheel position when not grounded
            if (visualWheel != null)
            {
                Vector3 localPos = visualWheel.localPosition;
                // Smoothly move wheel back to neutral position when in air
                localPos.y = Mathf.Lerp(localPos.y, 0, Time.fixedDeltaTime * 10f);
                visualWheel.localPosition = localPos;
            }
        }
    }
    
    void OnDrawGizmos()
    {
        // Draw wheel radius in the editor
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, wheelRadius);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * (suspensionDistance + wheelRadius));
    }

    void OnDrawGizmosSelected()
    {
        // Show wheel positions and rays in the Scene view
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.1f); // Wheel axle position
        Gizmos.DrawRay(transform.position, Vector3.down * (suspensionDistance + wheelRadius));
        
        // Draw the suspension range
        Gizmos.color = new Color(1, 0.5f, 0, 0.5f); // Orange, semi-transparent
        Gizmos.DrawWireSphere(transform.position + Vector3.down * suspensionDistance, wheelRadius);
        
        // Add this - visualize local axes
        if (visualWheel != null)
        {
            // Draw local axes of the wheel
            Gizmos.color = Color.red;
            Gizmos.DrawRay(visualWheel.position, visualWheel.right * 0.3f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(visualWheel.position, visualWheel.up * 0.3f);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(visualWheel.position, visualWheel.forward * 0.3f);
        }
    }
}