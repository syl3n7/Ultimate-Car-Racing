using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform target;
    public float smoothSpeed = 5f;
    public Vector3 offset = new Vector3(0, 5, -10);
    public bool lookAtTarget = true;
    
    [Header("Mouse Control")]
    public float mouseSensitivity = 2f;
    public float mouseControlTimeout = 5f;
    public float maxVerticalAngle = 80f;
    public float minVerticalAngle = -20f;
    public bool invertMouseY = false; // Option to invert Y axis
    
    [Header("Advanced Follow")]
    public float distanceDamping = 0.2f;
    public float heightDamping = 0.2f;
    public float rotationDamping = 1f;
    public float speedInfluence = 0.1f;
    public float maxSpeedForCameraPullback = 50f;
    
    [Header("Deadzone Settings")]
    public float positionDeadzone = 0.05f;
    public float rotationDeadzone = 0.5f;
    public float velocityDeadzone = 0.01f;
    
    [Header("Stabilization")]
    public float maxCameraTiltAngle = 5f;
    
    [Header("Mouse Cursor")]
    public bool lockCursorInGame = true;
    public KeyCode unlockCursorKey = KeyCode.Escape;

    private float lastMouseInputTime;
    private float currentRotationX;
    private float currentRotationY;
    private Vector3 initialOffset;
    private Vector3 velocity = Vector3.zero;
    private Vector3 lastTargetPosition;
    private Quaternion lastTargetRotation;
    private bool useMouseControl;
    private bool cursorLocked = false;
    private CarController targetCarController;
    
    void Start()
    {
        initialOffset = offset;
        currentRotationX = transform.eulerAngles.y;
        currentRotationY = transform.eulerAngles.x;
        if (target != null)
        {
            lastTargetPosition = target.position;
            lastTargetRotation = target.rotation;
        }
        
        // At start, make sure we're following the local player
        FindAndSetLocalPlayerTarget();
        
        // Lock cursor at start if option is enabled
        if (lockCursorInGame)
        {
            LockCursor();
        }
    }
    
    // Find and set the local player as the camera target
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
            
            // Initialize last positions after finding target
            if (target != null)
            {
                lastTargetPosition = target.position;
                lastTargetRotation = target.rotation;
            }
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
            AutoFollowCamera();
            StabilizeCamera();
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
            
        currentRotationX += mouseX;
        currentRotationY += mouseY;
        currentRotationY = Mathf.Clamp(currentRotationY, minVerticalAngle, maxVerticalAngle);
        
        Quaternion rotation = Quaternion.Euler(currentRotationY, currentRotationX, 0);
        Vector3 desiredPosition = target.position + rotation * initialOffset;
        
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, distanceDamping);
        
        // Look directly at target without smoothing during mouse control
        transform.LookAt(target);
    }
    
    void AutoFollowCamera()
    {
        // Always update position regardless of deadzone for smooth movement
        Vector3 targetForward = target.forward;
        targetForward.y = 0;
        targetForward.Normalize();

        // Calculate desired position
        Vector3 desiredPosition = target.position 
            - targetForward * initialOffset.z 
            + Vector3.up * offset.y 
            + target.right * initialOffset.x;

        // Apply smoothing
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, 
            ref velocity, distanceDamping);

        // Handle rotation
        if (lookAtTarget)
        {
            Vector3 lookTarget = target.position + Vector3.up * (offset.y * 0.3f);
            Vector3 lookDirection = lookTarget - transform.position;
            
            if (lookDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 
                    rotationDamping * Time.deltaTime * 5f);
            }
        }
        else
        {
            // Match target's y rotation only
            float targetYRotation = target.eulerAngles.y;
            transform.rotation = Quaternion.Slerp(transform.rotation, 
                Quaternion.Euler(transform.eulerAngles.x, targetYRotation, transform.eulerAngles.z), 
                rotationDamping * Time.deltaTime * 5f);
        }

        // Update last known positions
        lastTargetPosition = target.position;
        lastTargetRotation = target.rotation;
    }
    
    void StabilizeCamera()
    {
        // Keep camera relatively level by zeroing out roll
        transform.rotation = Quaternion.Euler(
            transform.eulerAngles.x,
            transform.eulerAngles.y,
            0);
    }
    
    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null) return;
        
        target = newTarget;
        
        // Cache reference to car controller
        targetCarController = target.GetComponent<CarController>();
        
        if (target != null)
        {
            // Initialize camera position based on target's current orientation
            Vector3 targetForward = target.forward;
            targetForward.y = 0;
            targetForward.Normalize();
            
            transform.position = target.position 
                - targetForward * initialOffset.z 
                + Vector3.up * offset.y 
                + target.right * initialOffset.x;
                
            transform.LookAt(target.position + Vector3.up * (offset.y * 0.3f));
            
            lastTargetPosition = target.position;
            lastTargetRotation = target.rotation;
            velocity = Vector3.zero;
            
            // Reset camera control values
            currentRotationX = transform.eulerAngles.y;
            currentRotationY = transform.eulerAngles.x;
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
}