using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Target")]
    public Transform target;
    
    [Header("GTA V Style Camera Offset (Easy 3-Axis Control)")]
    [Tooltip("Camera position relative to car: X=Left/Right, Y=Up/Down, Z=Forward/Back")]
    public Vector3 cameraOffset = new Vector3(0, 1.8f, -3.5f); // X, Y, Z offset from car
    
    [Header("Look At Settings")]
    [Range(0f, 3f)]
    public float lookAtHeightOffset = 0.5f; // How much higher to look at on the car
    
    [Header("Camera Behavior")]
    public bool useFixedGTACamera = true; // Use completely fixed GTA V style camera
    public float positionSmoothing = 10f; // How smooth the camera movement is
    public float rotationSmoothing = 8f; // How smooth the camera rotation is
    
    [Header("Ground Protection")]
    public float minimumHeightAboveGround = 0.8f;
    public LayerMask groundLayerMask = 1;
    
    [Header("Runtime Controls")]
    [Tooltip("Keys to adjust camera offset in real-time")]
    public KeyCode resetCameraKey = KeyCode.R;
    public KeyCode adjustModeKey = KeyCode.C; // Hold to enable adjustment mode
    
    // Adjustment keys for 3-axis control
    [Header("3-Axis Adjustment Keys (Hold C + Key)")]
    public KeyCode moveForwardKey = KeyCode.W;    // Decrease Z (move closer)
    public KeyCode moveBackwardKey = KeyCode.S;   // Increase Z (move further)
    public KeyCode moveLeftKey = KeyCode.A;       // Decrease X (move left)
    public KeyCode moveRightKey = KeyCode.D;      // Increase X (move right)
    public KeyCode moveUpKey = KeyCode.Q;         // Increase Y (move up)
    public KeyCode moveDownKey = KeyCode.E;       // Decrease Y (move down)
    
    public float adjustmentStep = 0.1f;
    public bool showAdjustmentFeedback = true;
    
    [Header("Debug")]
    public bool showCameraDistance = false;
    public bool showDebugInfo = false;

    // Private variables
    private CarController targetCarController;
    private Vector3 velocity = Vector3.zero;
    
    void Start()
    {
        // IMPORTANT: Detach camera from any parent to ensure world space operation
        if (transform.parent != null)
        {
            Debug.Log($"📹 Camera was parented to {transform.parent.name}. Detaching to ensure world space operation.");
            transform.SetParent(null);
        }
        
        // Find and set the local player as target
        FindAndSetLocalPlayerTarget();
        
        // Initialize camera position if we have a target
        if (target != null)
        {
            PositionCameraBehindTarget();
        }
    }
    
    void FindAndSetLocalPlayerTarget()
    {
        // Find the CORRECT player object - must have CarController component and be on Player layer (7)
        GameObject[] allPlayerTaggedObjects = GameObject.FindGameObjectsWithTag("Player");
        GameObject localPlayerObj = null;
        
        Debug.Log($"📹 Found {allPlayerTaggedObjects.Length} objects with 'Player' tag. Searching for the main car object...");
        
        foreach (GameObject obj in allPlayerTaggedObjects)
        {
            // Check if this object has CarController component AND is on the Player layer (layer 7)
            CarController carController = obj.GetComponent<CarController>();
            if (carController != null && obj.layer == 7)
            {
                localPlayerObj = obj;
                Debug.Log($"📹 Found MAIN player car object: {obj.name} (has CarController and is on Player layer)");
                break;
            }
            else
            {
                Debug.Log($"📹 Skipping '{obj.name}' - CarController: {(carController != null ? "YES" : "NO")}, Layer: {obj.layer} (need layer 7)");
            }
        }
        
        if (localPlayerObj != null)
        {
            SetTarget(localPlayerObj.transform);
            Debug.Log($"📹 ✅ Successfully set camera target to: {localPlayerObj.name}");
        }
        else
        {
            Debug.LogWarning("📹 ⚠️ No suitable player car object found! Make sure your player car has 'Player' tag, CarController component, and is on layer 7.");
        }
    }
    
    void Update()
    {
        if (target == null)
        {
            FindAndSetLocalPlayerTarget();
            return;
        }
        
        // Handle runtime controls
        HandleRuntimeControls();
        
        // Update camera position and rotation
        UpdateCameraPosition();
        
        if (showCameraDistance)
        {
            DisplayCameraInfo();
        }
    }
    
    void HandleRuntimeControls()
    {
        // Reset camera
        if (Input.GetKeyDown(resetCameraKey))
        {
            ResetToGTAVPosition();
        }
        
        // 3-Axis adjustment mode (hold C + WASD/QE)
        if (Input.GetKey(adjustModeKey))
        {
            bool adjustmentMade = false;
            Vector3 originalOffset = cameraOffset;
            
            // Forward/Backward (Z-axis)
            if (Input.GetKey(moveForwardKey))
            {
                cameraOffset.z += adjustmentStep; // Move closer (less negative)
                adjustmentMade = true;
            }
            if (Input.GetKey(moveBackwardKey))
            {
                cameraOffset.z -= adjustmentStep; // Move further (more negative)
                adjustmentMade = true;
            }
            
            // Left/Right (X-axis)
            if (Input.GetKey(moveLeftKey))
            {
                cameraOffset.x -= adjustmentStep; // Move left
                adjustmentMade = true;
            }
            if (Input.GetKey(moveRightKey))
            {
                cameraOffset.x += adjustmentStep; // Move right
                adjustmentMade = true;
            }
            
            // Up/Down (Y-axis)
            if (Input.GetKey(moveUpKey))
            {
                cameraOffset.y += adjustmentStep; // Move up
                adjustmentMade = true;
            }
            if (Input.GetKey(moveDownKey))
            {
                cameraOffset.y -= adjustmentStep; // Move down
                adjustmentMade = true;
            }
            
            // Apply constraints
            cameraOffset.y = Mathf.Clamp(cameraOffset.y, 0.2f, 10f); // Keep reasonable height
            cameraOffset.z = Mathf.Clamp(cameraOffset.z, -15f, 2f); // Keep reasonable distance
            cameraOffset.x = Mathf.Clamp(cameraOffset.x, -5f, 5f); // Keep reasonable side offset
            
            // Show feedback
            if (adjustmentMade && showAdjustmentFeedback)
            {
                Debug.Log($"📹 Camera Offset: X={cameraOffset.x:F1} Y={cameraOffset.y:F1} Z={cameraOffset.z:F1}");
            }
        }
    }
    
    void UpdateCameraPosition()
    {
        if (target == null) return;
        
        if (useFixedGTACamera)
        {
            UpdateGTAVStyleCamera();
        }
        else
        {
            UpdateTraditionalCamera();
        }
    }
    
    void UpdateGTAVStyleCamera()
    {
        // Calculate desired position based on car's transform and offset
        Vector3 desiredPosition = target.TransformPoint(cameraOffset);
        
        // Ground protection
        if (Physics.Raycast(desiredPosition, Vector3.down, out RaycastHit hit, 100f, groundLayerMask))
        {
            float groundHeight = hit.point.y + minimumHeightAboveGround;
            if (desiredPosition.y < groundHeight)
            {
                desiredPosition.y = groundHeight;
            }
        }
        
        // Smooth movement
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / positionSmoothing);
        
        // Calculate look at position
        Vector3 lookAtPosition = target.position + Vector3.up * lookAtHeightOffset;
        
        // Smooth rotation to look at target
        Vector3 direction = (lookAtPosition - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
        }
    }
    
    void UpdateTraditionalCamera()
    {
        // Traditional camera follow logic (fallback)
        Vector3 targetPosition = target.position + target.TransformDirection(cameraOffset);
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, 1f / positionSmoothing);
        
        Vector3 lookAtPosition = target.position + Vector3.up * lookAtHeightOffset;
        Vector3 direction = (lookAtPosition - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
        }
    }
    
    void PositionCameraBehindTarget()
    {
        if (target == null) return;
        
        // Position camera immediately behind the target
        Vector3 initialPosition = target.TransformPoint(cameraOffset);
        transform.position = initialPosition;
        
        // Look at the target
        Vector3 lookAtPosition = target.position + Vector3.up * lookAtHeightOffset;
        transform.LookAt(lookAtPosition);
        
        Debug.Log($"📹 Positioned camera behind target at: {initialPosition}");
    }
    
    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            Debug.LogWarning("📹 ⚠️ Attempted to set null target!");
            return;
        }
        
        // Enhanced validation: Check if the target has CarController component
        CarController carController = newTarget.GetComponent<CarController>();
        if (carController == null)
        {
            Debug.LogWarning($"📹 ⚠️ Target '{newTarget.name}' doesn't have CarController component! This might be a child object instead of the main car.");
            Debug.LogWarning("📹 Attempting to find the correct target...");
            FindAndSetLocalPlayerTarget();
            return;
        }
        
        target = newTarget;
        targetCarController = carController;
        
        // Position camera properly
        PositionCameraBehindTarget();
        
        Debug.Log($"📹 ✅ Camera target set to: {newTarget.name} with CarController");
    }
    
    public void ResetToGTAVPosition()
    {
        // Enhanced validation: verify target is correct
        if (target == null || target.GetComponent<CarController>() == null)
        {
            Debug.LogWarning("📹 ⚠️ Current target is invalid. Searching for correct target...");
            FindAndSetLocalPlayerTarget();
            if (target == null) return;
        }
        
        // Reset to GTA V style settings
        useFixedGTACamera = true;
        cameraOffset = new Vector3(0, 1.8f, -3.5f);
        
        // Position camera immediately
        PositionCameraBehindTarget();
        
        Debug.Log("📹 ✅ Camera reset to GTA V style position");
    }
    
    public void RestoreCursor()
    {
        // Restore cursor to normal state (unlocked and visible)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("📹 Cursor state restored to normal");
    }
    
    void DisplayCameraInfo()
    {
        if (target != null)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            Debug.Log($"📹 Distance to target: {distance:F1}m | Offset: {cameraOffset}");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (target != null && showDebugInfo)
        {
            // Draw connection line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, target.position);
            
            // Draw offset position
            Gizmos.color = Color.red;
            Vector3 offsetPos = target.TransformPoint(cameraOffset);
            Gizmos.DrawWireSphere(offsetPos, 0.5f);
            
            // Draw look at position
            Gizmos.color = Color.green;
            Vector3 lookAtPos = target.position + Vector3.up * lookAtHeightOffset;
            Gizmos.DrawWireSphere(lookAtPos, 0.3f);
        }
    }
}