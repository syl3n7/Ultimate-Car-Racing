using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltimateCarRacing.Networking;
using System.Diagnostics;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Car Settings")]
    public float maxSpeed = 30f;
    public float acceleration = 150f; // Adjusted torque value
    public float brakeForce = 300f;    // Adjusted brake power
    public float steeringAngle = 35f;  // Maximum steer angle in degrees

    [Header("Custom Wheels")]
    public CustomWheel frontLeftCustomWheel;  // Reference to the CustomWheel script on FL_Wheel_Physics
    public CustomWheel frontRightCustomWheel; // Reference to the CustomWheel script on FR_Wheel_Physics
    public CustomWheel rearLeftCustomWheel;   // Reference to the CustomWheel script on RL_Wheel_Physics
    public CustomWheel rearRightCustomWheel;  // Reference to the CustomWheel script on RR_Wheel_Physics

    [Header("Wheel Transforms")]
    public Transform frontLeftWheel;  // Reference to FL_Wheel_Visual
    public Transform frontRightWheel; // Reference to FR_Wheel_Visual
    public Transform rearLeftWheel;   // Reference to RL_Wheel_Visual
    public Transform rearRightWheel;  // Reference to RR_Wheel_Visual

    [Header("Suspension Settings")]
    public float suspensionDistance = 0.3f;
    public float springStrength = 30000f;
    public float damperStrength = 3000f;
    public float wheelRadius = 0.37f;

    [Header("Visual")]
    public GameObject driverModel;
    public TextMesh playerNameText;
    
    [Header("Network Smoothing")]
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 10f;
    public float velocityLerpSpeed = 5f;
    public float inputSmoothing = 0.2f;
    public float desyncThreshold = 5f; // Teleport if too far off

    // Expose these for the GameManager to read/write
    public Rigidbody Rigidbody { get; private set; }
    public bool IsLocal { get; private set; }
    public string PlayerId { get; private set; }
    public bool HasInputChanges { get; set; }
    
    // Current input values
    public float CurrentThrottle { get; private set; }
    public float CurrentSteering { get; private set; }
    public float CurrentBrake { get; private set; }
    
    // Network state for remote players
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetVelocity;
    private Vector3 targetAngularVelocity;
    
    // Smoothed input values for prediction
    private float targetThrottle;
    private float targetSteering;
    private float targetBrake;
    
    private float lastInputChangeTime;
    private bool isInitialized = false;
    
    // Network sync properties
    public float LastStateTimestamp { get; set; }
    public float LastInputTimestamp { get; set; }
    
    void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
        
        // Set a lower center of mass
        Rigidbody.centerOfMass = new Vector3(0, -0.5f, 0);
        
        // Increase gravity effect on the car
        Rigidbody.useGravity = true;
        Physics.gravity = new Vector3(0, -20.0f, 0); // Double gravity
        
        // Setup all wheels with the centralized settings
        SetupWheels();
    }

    public void Initialize(string playerId, bool isLocal)
    {
        if (isInitialized && PlayerId == playerId)
        {
            if (IsLocal != isLocal)
            {
                SetIsLocal(isLocal);
                SetupCamera();
                UnityEngine.Debug.Log($"Updated player {playerId} local status to {isLocal}");
            }
            return;
        }
        
        UnityEngine.Debug.Log($"Initializing player {playerId}, isLocal={isLocal}");
        PlayerId = playerId;
        IsLocal = isLocal;
        isInitialized = true;
        
        if (playerNameText != null)
        {
            playerNameText.text = playerId;
        }
        
        // Set colors for visibility
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = IsLocal ? Color.blue : Color.red;
        }
        else
        {
            foreach (Renderer childRenderer in GetComponentsInChildren<Renderer>())
            {
                childRenderer.material.color = IsLocal ? Color.blue : Color.red;
            }
        }
        
        SetupCamera();
        gameObject.name = IsLocal ? $"LocalCar_{playerId}" : $"RemoteCar_{playerId}";
        Rigidbody.WakeUp();
    }
    
    void Update()
    {
        if (!isInitialized) return;
        
        if (IsLocal)
        {
            HandleInput();
        }
        else
        {
            SmoothRemoteTransform();
        }
        
        UpdateWheels();
    }
    
    void FixedUpdate()
    {
        if (!isInitialized) return;
        
        if (IsLocal)
        {
            ApplyDriving();
        }
    }
    
    private void HandleInput()
    {
        if (!IsLocal)
        {
            UnityEngine.Debug.LogWarning($"HandleInput called on non-local player {PlayerId}");
            return;
        }

        float prevThrottle = CurrentThrottle;
        float prevSteering = CurrentSteering;
        float prevBrake = CurrentBrake;
        
        CurrentThrottle = Input.GetAxis("Vertical");
        CurrentSteering = Input.GetAxis("Horizontal");
        CurrentBrake = Input.GetKey(KeyCode.Space) ? 1f : 0f;
        
        if (Mathf.Abs(CurrentThrottle - prevThrottle) > 0.05f ||
            Mathf.Abs(CurrentSteering - prevSteering) > 0.05f ||
            Mathf.Abs(CurrentBrake - prevBrake) > 0.05f)
        {
            HasInputChanges = true;
            lastInputChangeTime = Time.time;
        }
    }
    
    // Replace the existing ApplyDriving method with this custom implementation
    private void ApplyDriving()
    {
        if (Rigidbody == null) return;
        
        float throttleInput = CurrentThrottle;
        float steeringInput = CurrentSteering;
        float brakeInput = CurrentBrake;
        
        // Get local velocity for determining forward/sideways motion
        Vector3 localVelocity = transform.InverseTransformDirection(Rigidbody.linearVelocity);
        
        // Calculate the car's forward speed
        float currentSpeed = localVelocity.z;
        float normalizedSpeed = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        
        // Apply steering forces - the faster you go, the less you can steer
        Vector3 steeringDirectionInitial = transform.right * steeringInput * (1.0f - normalizedSpeed * 0.5f);
        
        // Calculate wheel positions for force application
        Vector3 flPos = frontLeftWheel.position;
        Vector3 frPos = frontRightWheel.position;
        Vector3 rlPos = rearLeftWheel.position;
        Vector3 rrPos = rearRightWheel.position;
        
        // Visualize rotation of wheels based on steering
        if (frontLeftWheel != null && frontRightWheel != null)
        {
            // Determine the correct rotation axis for steering
            Vector3 steerAxis = Vector3.up; // Default world up axis
            
            // Apply steering rotation around the correct axis
            Quaternion steerRotation = Quaternion.AngleAxis(steeringInput * steeringAngle, steerAxis);
            
            // Apply the rotation to the wheel transforms
            // Note: We're using localRotation if the wheels are children of the car
            frontLeftWheel.localRotation = steerRotation;
            frontRightWheel.localRotation = steerRotation;
            
            // Visualize steering axis
            UnityEngine.Debug.DrawRay(frontLeftWheel.position, steerAxis * 0.5f, Color.yellow);
        }
        
        // Ground check and suspension (simplified)
        bool flGrounded = IsWheelGrounded(flPos, out Vector3 flNormal);
        bool frGrounded = IsWheelGrounded(frPos, out Vector3 frNormal);
        bool rlGrounded = IsWheelGrounded(rlPos, out Vector3 rlNormal);
        bool rrGrounded = IsWheelGrounded(rrPos, out Vector3 rrNormal);
        
        // Calculate wheel rotations for visual effect
        float wheelRadius = 0.37f; // FIXED: Use consistent wheel radius
        float wheelCircumference = 2f * Mathf.PI * wheelRadius;
        float wheelRotationSpeed = (currentSpeed / wheelCircumference) * 360f;
        
        // Apply driving force (rear wheel drive)
        if (rlGrounded || rrGrounded)
        {
            // Apply driving force at the rear wheels
            Vector3 driveForce = transform.forward * throttleInput * acceleration;
            
            // Reduce force based on speed to implement max speed
            float speedFactor = 1.0f - Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
            driveForce *= speedFactor;
            
            if (rlGrounded) Rigidbody.AddForceAtPosition(driveForce, rlPos, ForceMode.Force);
            if (rrGrounded) Rigidbody.AddForceAtPosition(driveForce, rrPos, ForceMode.Force);
        }
        
        // Apply steering forces (front wheels)
        if (flGrounded || frGrounded)
        {
            // Calculate steering direction based on car's current orientation
            Vector3 steeringDirection = transform.right * steeringInput; 
            
            // Calculate steering force based on car's forward direction and steering input
            Vector3 steeringForce = Vector3.Cross(steeringDirection.normalized, Vector3.up) * acceleration * 0.3f;
            
            // Debug visualization
            UnityEngine.Debug.DrawRay(transform.position, steeringDirection, Color.cyan);
            UnityEngine.Debug.DrawRay(transform.position, steeringForce, Color.magenta);
            
            if (flGrounded) Rigidbody.AddForceAtPosition(steeringForce, flPos, ForceMode.Force);
            if (frGrounded) Rigidbody.AddForceAtPosition(steeringForce, frPos, ForceMode.Force);
        }
        
        // Apply braking force to all wheels
        if (brakeInput > 0)
        {
            // Braking force is applied opposite to velocity direction
            Vector3 brakeForceVector = -Rigidbody.linearVelocity.normalized * brakeForce * brakeInput;
            
            if (flGrounded) Rigidbody.AddForceAtPosition(brakeForceVector, flPos, ForceMode.Force);
            if (frGrounded) Rigidbody.AddForceAtPosition(brakeForceVector, frPos, ForceMode.Force);
            if (rlGrounded) Rigidbody.AddForceAtPosition(brakeForceVector, rlPos, ForceMode.Force);
            if (rrGrounded) Rigidbody.AddForceAtPosition(brakeForceVector, rrPos, ForceMode.Force);
        }
        
        // Apply friction/grip forces
        ApplyWheelFriction(flPos, flGrounded, flNormal);
        ApplyWheelFriction(frPos, frGrounded, frNormal);
        ApplyWheelFriction(rlPos, rlGrounded, rlNormal);
        ApplyWheelFriction(rrPos, rrGrounded, rrNormal);
        
        // Apply downforce (increases with speed)
        float downforce = 3000f + (normalizedSpeed * 2000f); // Constant base downforce + speed-based component
        Rigidbody.AddForce(-transform.up * downforce, ForceMode.Force);

        // Add stabilization torque to prevent tipping
        Vector3 carUp = transform.up;
        Vector3 worldUp = Vector3.up;
        float rightDot = Vector3.Dot(carUp, worldUp);
        float stabilizationFactor = 1.0f - rightDot; // 0 when upright, 1 when sideways

        if (stabilizationFactor > 0.1f) // Only apply when tilting
        {
            // Calculate stabilization torque (tries to align car up with world up)
            Vector3 stabilizationTorque = Vector3.Cross(carUp, worldUp) * stabilizationFactor * 5000f;
            Rigidbody.AddTorque(stabilizationTorque, ForceMode.Force);
            
            // Debug visualization
            UnityEngine.Debug.DrawRay(transform.position, stabilizationTorque.normalized * 2f, Color.magenta);
        }

        // Add this to your ApplyDriving method:

        // Calculate wheel spin based on car's velocity
        float forwardSpeed = Vector3.Dot(Rigidbody.linearVelocity, transform.forward);
        // Reuse the existing wheelCircumference variable
        float rotationSpeed = (forwardSpeed / wheelCircumference) * 360f; // Degrees per second

        // Apply rotation to wheels - properly using the wheel's own forward axis
        if (frontLeftWheel != null) 
            frontLeftWheel.Rotate(frontLeftWheel.right, rotationSpeed * Time.fixedDeltaTime, Space.World);
        if (frontRightWheel != null)
            frontRightWheel.Rotate(frontRightWheel.right, rotationSpeed * Time.fixedDeltaTime, Space.World);
        if (rearLeftWheel != null)
            rearLeftWheel.Rotate(rearLeftWheel.right, rotationSpeed * Time.fixedDeltaTime, Space.World);
        if (rearRightWheel != null)
            rearRightWheel.Rotate(rearRightWheel.right, rotationSpeed * Time.fixedDeltaTime, Space.World);
    }

    // Helper method to check if a wheel is touching the ground
    private bool IsWheelGrounded(Vector3 wheelPosition, out Vector3 groundNormal)
    {
        float rayDistance = 1.0f;
        int layerMask = 1 << 3; // Use ONLY the ground layer (layer 3)
        
        RaycastHit hit;
        // FIXED: Use Vector3.down instead of -transform.up to ensure consistent Y-axis checking
        if (Physics.Raycast(wheelPosition, Vector3.down, out hit, rayDistance, layerMask))
        {
            groundNormal = hit.normal;
            UnityEngine.Debug.DrawLine(wheelPosition, hit.point, Color.green);
            return true;
        }
        
        // Second raycast is already world space
        if (Physics.Raycast(wheelPosition, Vector3.down, out hit, rayDistance, layerMask))
        {
            groundNormal = hit.normal;
            UnityEngine.Debug.DrawLine(wheelPosition, hit.point, Color.yellow);
            return true;
        }
        
        groundNormal = Vector3.up;
        return false;
    }

    // Apply friction to keep the car from sliding sideways
    private void ApplyWheelFriction(Vector3 wheelPosition, bool isGrounded, Vector3 groundNormal)
    {
        if (!isGrounded) return;
        
        // Get the local velocity at the wheel position
        Vector3 wheelVelocity = Rigidbody.GetPointVelocity(wheelPosition);
        Vector3 wheelLocalVel = transform.InverseTransformDirection(wheelVelocity);
        
        // Calculate lateral (sideways) velocity component
        Vector3 lateralVelocity = transform.right * wheelLocalVel.x;
        
        // Calculate friction force (opposite to lateral velocity)
        float frictionCoefficient = 2.0f; // Increased from 0.5f
        Vector3 frictionForce = -lateralVelocity * frictionCoefficient * Rigidbody.mass;
        
        // Apply the friction force at the wheel position
        Rigidbody.AddForceAtPosition(frictionForce, wheelPosition, ForceMode.Force);
    }

    // Spin the wheel mesh based on current speed
    private void RotateWheel(Transform wheel, float rotationSpeed)
    {
        if (wheel == null) return;
        
        // Add rotation around local X axis (forward motion)
        wheel.Rotate(rotationSpeed * Time.deltaTime, 0, 0, Space.Self);
    }

    // Replace the UpdateWheels method with this simplified version
    private void UpdateWheels()
    {
        // We're now handling wheel visual updates manually in ApplyDriving
        // This method could be removed or used for additional wheel effects
    }
    
    // For remote players – smooth transforms (unchanged)
    private void SmoothRemoteTransform()
    {
        float lerpFactor = Time.deltaTime * positionLerpSpeed;
        if (Vector3.Distance(transform.position, targetPosition) > desyncThreshold)
        {
            transform.position = targetPosition;
            Rigidbody.linearVelocity = targetVelocity;
            transform.rotation = targetRotation;
            Rigidbody.angularVelocity = targetAngularVelocity;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, lerpFactor);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lerpFactor);
            Rigidbody.linearVelocity = Vector3.Lerp(Rigidbody.linearVelocity, targetVelocity, velocityLerpSpeed * Time.deltaTime);
            Rigidbody.angularVelocity = Vector3.Lerp(Rigidbody.angularVelocity, targetAngularVelocity, velocityLerpSpeed * Time.deltaTime);
        }
        
        CurrentThrottle = Mathf.Lerp(CurrentThrottle, targetThrottle, inputSmoothing);
        CurrentSteering = Mathf.Lerp(CurrentSteering, targetSteering, inputSmoothing);
        CurrentBrake = Mathf.Lerp(CurrentBrake, targetBrake, inputSmoothing);
    }
    
    public void SetupCamera()
    {
        Camera carCamera = GetComponentInChildren<Camera>(true);
        if (carCamera != null)
        {
            UnityEngine.Debug.Log($"Found camera on {PlayerId}, isLocal={IsLocal}");
            
            if (IsLocal)
            {
                carCamera.gameObject.SetActive(true);
                carCamera.tag = "MainCamera";
                AudioListener[] listeners = FindObjectsOfType<AudioListener>();
                foreach (AudioListener listener in listeners)
                {
                    if (listener.gameObject != carCamera.gameObject)
                        listener.enabled = false;
                }
                AudioListener carAudioListener = carCamera.GetComponent<AudioListener>();
                if (carAudioListener != null)
                    carAudioListener.enabled = true;
                else
                    carCamera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                carCamera.gameObject.SetActive(false);
                AudioListener audioListener = carCamera.GetComponent<AudioListener>();
                if (audioListener != null)
                    audioListener.enabled = false;
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning($"No camera found on player {PlayerId}!");
        }
    }
    
    public void SetIsLocal(bool isLocal)
    {
        var field = this.GetType().GetField("IsLocal", 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.NonPublic);
        
        if (field != null)
            field.SetValue(this, isLocal);
        else
            Initialize(this.PlayerId, isLocal);
    }
    
    // Modify the ApplyRemoteState method to support teleporting
    public void ApplyRemoteState(GameManager.PlayerStateData stateData, bool teleport = false)
    {
        if (teleport)
        {
            // Teleport immediately to the new state
            transform.position = stateData.Position;
            transform.rotation = Quaternion.Euler(stateData.Rotation);
            Rigidbody.linearVelocity = stateData.Velocity;
            Rigidbody.angularVelocity = stateData.AngularVelocity;
        }
        else
        {
            // Update target state for smooth interpolation
            targetPosition = stateData.Position;
            targetRotation = Quaternion.Euler(stateData.Rotation);
            targetVelocity = stateData.Velocity;
            targetAngularVelocity = stateData.AngularVelocity;
        }
    }
    
    public void ApplyRemoteInput(GameManager.PlayerInputData inputData)
    {
        // Apply input from remote player for prediction
        targetThrottle = inputData.Throttle;
        targetSteering = inputData.Steering;
        targetBrake = inputData.Brake;
    }
    
    public void Respawn(Vector3 position, Quaternion rotation)
    {
        // Reset physics state
        Rigidbody.linearVelocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;
        
        // Set new position and rotation
        transform.position = position;
        transform.rotation = rotation;
        
        // Update target position for remote players
        targetPosition = position;
        targetRotation = rotation;
        targetVelocity = Vector3.zero;
        targetAngularVelocity = Vector3.zero;
    }

    void OnDrawGizmos()
    {
        // Draw a colored sphere to show who's who
        if (Application.isPlaying && isInitialized)
        {
            Gizmos.color = IsLocal ? Color.blue : Color.red;
            Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.5f);
            
            // Draw text to show player ID
            #if UNITY_EDITOR
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, 
                $"{PlayerId} (Local: {IsLocal})");
            #endif
        }
    }

    void OnDestroy()
    {
        UnityEngine.Debug.Log($"[CRITICAL] PlayerController for {PlayerId} (isLocal: {IsLocal}) is being destroyed!");
    
        // If this is a local player being destroyed, log the stack trace
        if (IsLocal)
        {
            UnityEngine.Debug.LogError($"LOCAL PLAYER DESTROYED! Stack trace:\n{System.Environment.StackTrace}");
            
            // Also log who is calling Destroy
            StackTrace stackTrace = new StackTrace(true);
            foreach (StackFrame frame in stackTrace.GetFrames())
            {
                UnityEngine.Debug.LogError($"Frame: {frame.GetMethod().Name} in {frame.GetFileName()} at line {frame.GetFileLineNumber()}");
            }
        }
    }

    // Add a version of OnGUI to the PlayerController
    void OnGUI()
    {
        // Only show for the local player or if in debug mode
        if (!IsLocal) return;
        
        // Display connection and sync stats at the bottom of the screen
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.yellow;
        style.fontSize = 12;
        style.padding = new RectOffset(5, 5, 5, 5);
        
        string latency = NetworkManager.Instance != null ? 
            $"{NetworkManager.Instance.GetAverageLatency() * 1000:F0}ms" : "N/A";
        
        GUI.Box(new Rect(10, Screen.height - 70, 300, 60), "");
        
        GUI.Label(new Rect(15, Screen.height - 65, 290, 20),
            $"Network Stats: Latency: {latency}", style);
        
        GUI.Label(new Rect(15, Screen.height - 45, 290, 20),
            $"Players: {PlayerId} (Local: {IsLocal})", style);
        
        GUI.Label(new Rect(15, Screen.height - 25, 290, 20),
            $"Position: {transform.position.ToString("F1")}", style);
    }

    // Add this method to your PlayerController to set up the wheels
    private void SetupWheels()
    {
        CustomWheel[] wheels = new CustomWheel[] { 
            frontLeftCustomWheel, frontRightCustomWheel, 
            rearLeftCustomWheel, rearRightCustomWheel 
        };
        
        foreach (var wheel in wheels)
        {
            if (wheel != null)
            {
                // Pass down settings from the PlayerController, 
                // BUT respect manually set wheel radius
                wheel.suspensionDistance = this.suspensionDistance;
                wheel.springStrength = this.springStrength;
                wheel.damperStrength = this.damperStrength;
                
                // MODIFIED: Don't override wheel radius if it already has a reasonable value
                if (wheel.wheelRadius <= 0.01f || wheel.wheelRadius > 1.0f)
                {
                    UnityEngine.Debug.Log($"Setting wheel radius on {wheel.name} to {this.wheelRadius} (from PlayerController)");
                    wheel.wheelRadius = this.wheelRadius;
                }
                else
                {
                    UnityEngine.Debug.Log($"Keeping manually set wheel radius on {wheel.name}: {wheel.wheelRadius}");
                }
                
                // Setup visual wheel reference if not already set
                if (wheel.visualWheel == null)
                {
                    // Try to assign the corresponding transform
                    if (wheel == frontLeftCustomWheel && frontLeftWheel != null)
                        wheel.visualWheel = frontLeftWheel;
                    else if (wheel == frontRightCustomWheel && frontRightWheel != null)
                        wheel.visualWheel = frontRightWheel;
                    else if (wheel == rearLeftCustomWheel && rearLeftWheel != null)
                        wheel.visualWheel = rearLeftWheel;
                    else if (wheel == rearRightCustomWheel && rearRightWheel != null)
                        wheel.visualWheel = rearRightWheel;
                }
                
                // Make sure the ground mask is set
                if (wheel.groundMask.value == 0)
                    wheel.groundMask = 1 << 3; // Layer 3 is "Ground"
            }
        }
    }

    public void TuneSuspension(float springMultiplier, float damperMultiplier)
    {
        // Apply multipliers to current settings
        float newSpring = springStrength * springMultiplier;
        float newDamper = damperStrength * damperMultiplier;
        
        UnityEngine.Debug.Log($"Tuning suspension: Spring {springStrength} → {newSpring}, Damper {damperStrength} → {newDamper}");
        
        // Update local values
        springStrength = newSpring;
        damperStrength = newDamper;
        
        // Update all wheels
        SetupWheels();
    }
}