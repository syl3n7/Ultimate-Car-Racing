using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("GTA V Style Camera Settings")]
    public Transform target;
    
    [Header("Follow Distance & Position")]
    public float followDistance = 8f;
    public float followHeight = 3f;
    public float heightOffset = 1.5f; // How much higher to look at on the car
    
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
        Vector3 desiredPosition = target.position + rotation * new Vector3(0, followHeight, -followDistance);
        
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / positionSmoothing);
        
        // Look at target with height offset
        Vector3 lookTarget = target.position + Vector3.up * heightOffset;
        transform.LookAt(lookTarget);
    }
    
    void GTAStyleAutoFollow()
    {
        // Get car's velocity for speed-based effects
        float carSpeed = 0f;
        Vector3 carVelocity = Vector3.zero;
        if (targetCarController != null && targetCarController.GetComponent<Rigidbody>() != null)
        {
            carVelocity = targetCarController.GetComponent<Rigidbody>().linearVelocity;
            carSpeed = carVelocity.magnitude;
        }
        
        // Calculate dynamic follow distance based on speed
        float dynamicDistance = followDistance + Mathf.Min(carSpeed * speedPullbackMultiplier, maxSpeedPullback);
        
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
        
        // Calculate desired position behind the car
        Vector3 targetForward = target.forward;
        Vector3 desiredPosition = target.position - targetForward * dynamicDistance + Vector3.up * followHeight;
        
        // Smooth position movement
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 1f / positionSmoothing);
        
        // Calculate look target with slight anticipation
        Vector3 lookTarget = target.position + Vector3.up * heightOffset;
        
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
        
        Vector3 targetForward = target.forward;
        transform.position = target.position - targetForward * followDistance + Vector3.up * followHeight;
        
        Vector3 lookTarget = target.position + Vector3.up * heightOffset;
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
}