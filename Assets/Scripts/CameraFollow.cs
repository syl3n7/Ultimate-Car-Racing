using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("GTA V Style Camera Settings")]
    public Transform target;
    
    [Header("Follow Distance & Position")]
    [Range(2f, 20f)]
    public float horizontalDistance = 8f; // How far back behind the car
    [Range(0.5f, 10f)]
    public float verticalHeight = 3f; // How high above the car
    [Range(0f, 5f)]
    public float lookAtHeightOffset = 1.5f; // How much higher to look at on the car
    
    [Header("Simple Offset-Based Following")]
    public bool useSimpleOffset = true; // Use the simple, reliable TransformPoint method
    public Vector3 cameraOffset = new Vector3(0, 3f, -8f); // Local offset from car (X, Y, Z)
    
    [Header("Debug")]
    public bool showDebugInfo = false; // Show debug information in console
    
    [Header("Runtime Controls (Optional)")]
    public KeyCode increaseDistanceKey = KeyCode.Plus;
    public KeyCode decreaseDistanceKey = KeyCode.Minus;
    public KeyCode increaseHeightKey = KeyCode.PageUp;
    public KeyCode decreaseHeightKey = KeyCode.PageDown;
    public float adjustmentStep = 0.5f;
    public bool showAdjustmentFeedback = true;
    
    [Header("Smoothing")]
    public float positionSmoothing = 3f;
    public float rotationSmoothing = 2f;
    public float bankingSmoothing = 1.5f;
    
    [Header("Banking & Tilting")]
    public float maxBankAngle = 8f; // Maximum banking when turning
    public float bankingSensitivity = 1f;
    
    [Header("Speed Effects")]
    public float speedPullbackMultiplier = 0.02f; // How much further back at high speed
    public float maxSpeedPullback = 3f;
    
    [Header("Mouse Control")]
    public float mouseSensitivity = 2f;
    public float mouseControlTimeout = 3f;
    public float maxVerticalAngle = 45f;
    public float minVerticalAngle = -15f;
    public bool invertMouseY = false;
    
    [Header("Mouse Cursor")]
    public bool lockCursorInGame = true;
    public KeyCode unlockCursorKey = KeyCode.Escape;

    // Private variables
    private float lastMouseInputTime;
    private float currentMouseX;
    private float currentMouseY;
    private bool useMouseControl;
    private bool cursorLocked = false;
    private CarController targetCarController;
    private float currentBankAngle = 0f;
    private Vector3 velocity = Vector3.zero;
    
    void Start()
    {
        // IMPORTANT: Detach camera from any parent to ensure world space operation
        if (transform.parent != null)
        {
            Debug.Log($"Camera was parented to {transform.parent.name}. Detaching to ensure world space operation.");
            transform.SetParent(null);
        }
        
        // Initialize camera offset to match current settings
        cameraOffset = new Vector3(0, verticalHeight, -horizontalDistance);
        
        // Find and set the local player as target
        FindAndSetLocalPlayerTarget();
        
        // Lock cursor at start if option is enabled
        if (lockCursorInGame)
        {
            LockCursor();
        }
        
        // Initialize camera position if we have a target
        if (target != null)
        {
            PositionCameraBehindTarget();
        }
    }
    
    void FindAndSetLocalPlayerTarget()
    {
        // Find local player object with tag
        GameObject localPlayerObj = GameObject.FindWithTag("Player");
        if (localPlayerObj != null)
        {
            target = localPlayerObj.transform;
            Debug.Log($"Camera set to follow local player: {localPlayerObj.name}");
            
            // Cache reference to car controller
            targetCarController = localPlayerObj.GetComponent<CarController>();
        }
        else
        {
            Debug.LogWarning("Cannot find a GameObject tagged as 'Player'!");
        }
    }
    
    void Update()
    {
        // Handle cursor locking/unlocking
        if (Input.GetKeyDown(unlockCursorKey))
        {
            if (cursorLocked)
                UnlockCursor();
            else
                LockCursor();
        }
        
        // Handle runtime camera adjustments
        HandleRuntimeCameraAdjustments();
        
        // Check for active input fields before processing mouse input
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
        {
            // Check if selected object has an input field component
            if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null ||
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
            {
                // If an input field is active, don't process mouse input for camera
                return;
            }
        }
    }
    
    void LateUpdate()
    {
        // If target is null or tagged as remote player, try to find local player again
        if (target == null || (target != null && target.CompareTag("RemotePlayer")))
        {
            FindAndSetLocalPlayerTarget();
            
            // If still no target, exit early
            if (target == null)
                return;
        }
            
        HandleMouseInput();
        
        useMouseControl = Time.time - lastMouseInputTime < mouseControlTimeout;
        
        if (useMouseControl)
        {
            MouseControlCamera();
        }
        else
        {
            GTAStyleAutoFollow();
        }
    }
    
    void HandleMouseInput()
    {
        if (Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0)
        {
            lastMouseInputTime = Time.time;
        }
    }
    
    void MouseControlCamera()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // Apply inversion if enabled
        if (invertMouseY)
            mouseY = -mouseY;
        else
            mouseY = -mouseY; // Default behavior is already inverted
            
        currentMouseX += mouseX;
        currentMouseY += mouseY;
        currentMouseY = Mathf.Clamp(currentMouseY, minVerticalAngle, maxVerticalAngle);
        
        // Calculate position based on mouse rotation
        Quaternion rotation = Quaternion.Euler(currentMouseY, currentMouseX, 0);
        // Simple positioning: always behind and above the car
        Vector3 offset = new Vector3(0, verticalHeight, horizontalDistance);
        Vector3 desiredPosition = target.position + rotation * offset;
        
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / positionSmoothing);
        
        // Look at target with height offset
        Vector3 lookTarget = target.position + Vector3.up * lookAtHeightOffset;
        transform.LookAt(lookTarget);
    }
    
    void GTAStyleAutoFollow()
    {
        // INTELLIGENT FAILSAFE: Only detach if camera is actually a child of the target car
        // This prevents the camera from being incorrectly parented but allows intentional setups
        if (transform.parent == target)
        {
            Debug.LogWarning("Camera was directly parented to target car - detaching for independent operation");
            transform.SetParent(null);
        }
        
        // Get car's velocity for speed-based effects
        float carSpeed = 0f;
        Vector3 carVelocity = Vector3.zero;
        if (targetCarController != null && targetCarController.GetComponent<Rigidbody>() != null)
        {
            carVelocity = targetCarController.GetComponent<Rigidbody>().linearVelocity;
            carSpeed = carVelocity.magnitude;
        }
        
        // Calculate dynamic follow distance based on speed
        float dynamicDistance = horizontalDistance + Mathf.Min(carSpeed * speedPullbackMultiplier, maxSpeedPullback);
        
        // Calculate banking angle based on car's turning
        float targetBankAngle = 0f;
        if (targetCarController != null)
        {
            // Use the car's angular velocity to determine banking
            Vector3 angularVel = targetCarController.GetComponent<Rigidbody>().angularVelocity;
            targetBankAngle = -angularVel.y * bankingSensitivity * maxBankAngle;
            targetBankAngle = Mathf.Clamp(targetBankAngle, -maxBankAngle, maxBankAngle);
        }
        
        // Smooth the banking angle
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBankAngle, bankingSmoothing * Time.deltaTime);
        
        // SIMPLE & RELIABLE: Use TransformPoint method like your example
        Vector3 desiredPosition;
        
        if (useSimpleOffset)
        {
            // Method 1: Your simple, reliable approach using TransformPoint
            // Update the offset based on current settings
            Vector3 dynamicOffset = new Vector3(cameraOffset.x, verticalHeight, -dynamicDistance);
            desiredPosition = target.TransformPoint(dynamicOffset);
        }
        else
        {
            // Method 2: Fallback to manual calculation if needed
            Vector3 backwardDirection = -target.forward;
            Vector3 horizontalPosition = target.position + backwardDirection * dynamicDistance;
            desiredPosition = horizontalPosition + Vector3.up * verticalHeight;
        }
        
        // Apply smooth movement
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / positionSmoothing);
        
        // Calculate look target with slight anticipation
        Vector3 lookTarget = target.position + Vector3.up * lookAtHeightOffset;
        
        // Add slight forward anticipation based on car velocity
        if (carVelocity.magnitude > 5f)
        {
            Vector3 anticipation = carVelocity.normalized * Mathf.Min(carSpeed * 0.1f, 2f);
            lookTarget += anticipation;
        }
        
        // Calculate rotation with banking
        Vector3 lookDirection = lookTarget - transform.position;
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            
            // Apply banking (roll rotation)
            targetRotation *= Quaternion.Euler(0, 0, currentBankAngle);
            
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
        }
    }
    
    void PositionCameraBehindTarget()
    {
        if (target == null) return;
        
        // Use simple and reliable positioning like your example
        if (useSimpleOffset)
        {
            Vector3 initialOffset = new Vector3(cameraOffset.x, verticalHeight, -horizontalDistance);
            transform.position = target.TransformPoint(initialOffset);
        }
        else
        {
            Vector3 backwardDirection = -target.forward;
            Vector3 horizontalPosition = target.position + backwardDirection * horizontalDistance;
            transform.position = horizontalPosition + Vector3.up * verticalHeight;
        }
        
        Vector3 lookTarget = target.position + Vector3.up * lookAtHeightOffset;
        transform.LookAt(lookTarget);
        
        // Reset mouse control values
        currentMouseX = transform.eulerAngles.y;
        currentMouseY = transform.eulerAngles.x;
        
        velocity = Vector3.zero;
        currentBankAngle = 0f;
    }
    
    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null) return;
        
        // IMPORTANT: Ensure camera is never parented to the target
        if (transform.parent != null)
        {
            Debug.Log($"Camera was parented to {transform.parent.name}. Detaching for independent operation.");
            transform.SetParent(null);
        }
        
        target = newTarget;
        
        // Cache reference to car controller
        targetCarController = target.GetComponent<CarController>();
        
        if (target != null)
        {
            PositionCameraBehindTarget();
        }
    }
    
    // Lock cursor to game window
    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorLocked = true;
    }
    
    // Unlock cursor
    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorLocked = false;
    }
    
    // Call this when exiting game or returning to menu
    public void RestoreCursor()
    {
        UnlockCursor();
    }
    
    void OnDestroy()
    {
        // Ensure cursor is restored when component is destroyed
        RestoreCursor();
    }

    void HandleRuntimeCameraAdjustments()
    {
        // Adjust horizontal distance (how far back behind the car)
        if (Input.GetKeyDown(increaseDistanceKey))
        {
            if (useSimpleOffset)
            {
                horizontalDistance = Mathf.Clamp(horizontalDistance + adjustmentStep, 2f, 20f);
                cameraOffset.z = -horizontalDistance; // Update the offset
            }
            else
            {
                horizontalDistance = Mathf.Clamp(horizontalDistance + adjustmentStep, 2f, 20f);
            }
            if (showAdjustmentFeedback)
                Debug.Log($"📹 Camera Distance: {horizontalDistance:F1}m (+ / - to adjust)");
        }
        if (Input.GetKeyDown(decreaseDistanceKey))
        {
            if (useSimpleOffset)
            {
                horizontalDistance = Mathf.Clamp(horizontalDistance - adjustmentStep, 2f, 20f);
                cameraOffset.z = -horizontalDistance; // Update the offset
            }
            else
            {
                horizontalDistance = Mathf.Clamp(horizontalDistance - adjustmentStep, 2f, 20f);
            }
            if (showAdjustmentFeedback)
                Debug.Log($"📹 Camera Distance: {horizontalDistance:F1}m (+ / - to adjust)");
        }
        
        // Adjust vertical height (how high above the car)
        if (Input.GetKeyDown(increaseHeightKey))
        {
            verticalHeight = Mathf.Clamp(verticalHeight + adjustmentStep, 0.5f, 10f);
            if (useSimpleOffset)
                cameraOffset.y = verticalHeight; // Update the offset
            if (showAdjustmentFeedback)
                Debug.Log($"📹 Camera Height: {verticalHeight:F1}m (PgUp / PgDn to adjust)");
        }
        if (Input.GetKeyDown(decreaseHeightKey))
        {
            verticalHeight = Mathf.Clamp(verticalHeight - adjustmentStep, 0.5f, 10f);
            if (useSimpleOffset)
                cameraOffset.y = verticalHeight; // Update the offset
            if (showAdjustmentFeedback)
                Debug.Log($"📹 Camera Height: {verticalHeight:F1}m (PgUp / PgDn to adjust)");
        }
    }
}