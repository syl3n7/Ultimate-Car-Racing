using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class CarController : MonoBehaviour 
{
    public Wheel[] wheels;
    public Vector2 moveInput;
    public float maxSteer = 30, wheelbase = 2.5f, trackwidth = 1.5f;
    public string playerId { get; set; }
    public bool isLocalPlayer { get; set; }
    
    // Simple driving parameters
    [Header("Driving Settings")]
    public float powerMultiplier = 2500f;
    public float brakeForce = 5000f;
    public float steeringSpeed = 10f; // How quickly steering responds
    public float returnToZeroSpeed = 15f; // How quickly steering returns to center
    
    // Engine properties
    [Header("Engine Settings")]
    [SerializeField] private float maxRPM = 8800f;
    [SerializeField] private float idleRPM = 800f;
    [SerializeField] private float[] gearRatios = { 3.82f, 2.26f, 1.64f, 1.29f, 1.06f, 0.84f, 0.62f };
    [SerializeField] private float finalDriveRatio = 3.44f;
    [SerializeField] private float reverseGearRatio = 3.67f;
    
    [Header("Engine Sounds")]
    public AudioClip engineIdleClip;
    public AudioClip engineRunningClip;
    public float minPitch = 0.7f;
    public float maxPitch = 1.5f;
    public float pitchMultiplier = 1f;
    private AudioSource engineAudioSource;
    
    // Speed properties
    public float currentSpeed { get; private set; }
    public float speedKmh { get; private set; }
    public float engineRPM { get; private set; }
    public int currentGear { get; private set; } = 1; // Start in first gear
    
    // Event for UI updates
    public delegate void CarStatsUpdated(float speed, float rpm, int gear);
    public event CarStatsUpdated OnCarStatsUpdated;
    
    // Internal variables
    private Rigidbody rb;
    private float currentSteerAngle = 0f;
    private float wheelCircumference;
    private float lastShiftTime;
    private float shiftDelay = 0.5f;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("CarController requires a Rigidbody component!");
        }
        
        // Calculate wheel circumference (assuming all wheels are same size)
        if (wheels.Length > 0)
        {
            wheelCircumference = 2 * Mathf.PI * wheels[0].collider.radius;
        }
        
        engineRPM = idleRPM;
        
        // Audio setup
        engineAudioSource = GetComponent<AudioSource>();
        if (engineAudioSource == null)
        {
            engineAudioSource = gameObject.AddComponent<AudioSource>();
        }
        
        engineAudioSource.spatialBlend = 1f; // Full 3D sound
        engineAudioSource.rolloffMode = AudioRolloffMode.Linear;
        engineAudioSource.minDistance = 5f;
        engineAudioSource.maxDistance = 30f;
        engineAudioSource.dopplerLevel = 0.5f;
        engineAudioSource.loop = true;
        engineAudioSource.clip = engineRunningClip;
        engineAudioSource.Play();
    }
    
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void EnableControls(bool enabled)
    {
        if (!enabled)
        {
            moveInput = Vector2.zero;
        }
        
        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = enabled;
        }
    }

    void Update()
    {
        UpdateEngineSound();
    }
    
    private void UpdateEngineSound()
    {
        if (engineAudioSource == null) return;
    
        // Calculate normalized RPM (0-1 range between idle and max RPM)
        float normalizedRPM = Mathf.InverseLerp(idleRPM, maxRPM, engineRPM);
    
        // Adjust pitch based on RPM
        engineAudioSource.pitch = Mathf.Lerp(minPitch, maxPitch, normalizedRPM) * pitchMultiplier;
    
        // Adjust volume based on throttle input
        engineAudioSource.volume = 0.3f + (Mathf.Abs(moveInput.y) * 0.7f);
    }

    void FixedUpdate() 
    {
        // Update speed metrics
        CalculateSpeed();
        
        // Handle automatic gear changes
        HandleGearChanges();
        
        // Calculate RPM for audio and UI
        CalculateRPM();
        
        // Apply motor forces to drive wheels
        ApplyDrivingForces();
        
        // Handle steering with smooth interpolation
        HandleSmoothSteering();
        
        // Update wheel visuals
        UpdateWheelVisuals();
    }
    
    private void ApplyDrivingForces()
    {
        // Get throttle input and clamp it
        float throttle = moveInput.y;
        
        if (throttle > 0) // Accelerating
        {
            // Apply driving force to appropriate wheels
            foreach (var wheel in wheels)
            {
                // Handle FWD, RWD, or AWD configurations
                if ((wheel.wheelType == WheelType.front) || (wheel.wheelType == WheelType.rear))
                {
                    wheel.collider.motorTorque = throttle * powerMultiplier / 2.0f; // Divide power between wheels
                    wheel.collider.brakeTorque = 0; // Release brakes when accelerating
                }
            }
        }
        else if (throttle < 0) // Braking or reversing
        {
            // Apply brakes to all wheels
            foreach (var wheel in wheels)
            {
                // Higher brake force on front wheels (realistic weight transfer)
                float brakePower = -throttle * brakeForce;
                if (wheel.wheelType == WheelType.front)
                {
                    brakePower *= 0.7f; // 70% braking on front
                }
                else
                {
                    brakePower *= 0.3f; // 30% braking on rear
                }
                
                wheel.collider.brakeTorque = brakePower;
                wheel.collider.motorTorque = 0; // No motor force when braking
            }
            
            // If we're almost stopped and still pressing brake, switch to reverse
            if (speedKmh < 1.0f && throttle < -0.5f && currentGear > 0)
            {
                currentGear = -1; // Reverse gear
            }
        }
        else // No input - let the car coast
        {
            foreach (var wheel in wheels)
            {
                wheel.collider.motorTorque = 0;
                wheel.collider.brakeTorque = 10f; // Light braking for natural slowdown
            }
        }
        
        // If in reverse gear but pressing accelerator, switch back to first gear when stopped
        if (currentGear == -1 && throttle > 0.5f && speedKmh < 1.0f)
        {
            currentGear = 1;
        }
    }
    
    private void HandleSmoothSteering()
    {
        // Calculate target steering angle based on input
        float targetSteerAngle = moveInput.x * maxSteer;
        
        // Smoothly interpolate current steering angle toward target
        if (Mathf.Abs(moveInput.x) > 0.01f)
        {
            // Moving toward the target steering angle
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, Time.fixedDeltaTime * steeringSpeed);
        }
        else
        {
            // Returning to center faster when no input
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, 0, Time.fixedDeltaTime * returnToZeroSpeed);
        }
        
        // Apply Ackermann steering geometry for realistic turning
        if (currentSteerAngle > 0) { // Turning right
            // Left wheel (index 0) gets the outer angle
            wheels[0].collider.steerAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / 
                (trackwidth + Mathf.Tan(Mathf.Deg2Rad * currentSteerAngle) * wheelbase));
                
            // Right wheel (index 1) gets the inner angle (full steer)
            wheels[1].collider.steerAngle = currentSteerAngle;
        } 
        else if (currentSteerAngle < 0) { // Turning left
            // Left wheel (index 0) gets the inner angle (full steer)
            wheels[0].collider.steerAngle = currentSteerAngle;
            
            // Right wheel (index 1) gets the outer angle
            float outerAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / 
                (trackwidth + Mathf.Tan(Mathf.Deg2Rad * Mathf.Abs(currentSteerAngle)) * wheelbase));
                
            wheels[1].collider.steerAngle = -outerAngle; // Negative sign for left turn on right wheel
        } 
        else {
            // No steering - both wheels straight
            wheels[0].collider.steerAngle = 0;
            wheels[1].collider.steerAngle = 0;
        }
    }
    
    private void HandleGearChanges()
    {
        // Don't shift too frequently
        if (Time.time - lastShiftTime < shiftDelay) return;
        
        // Automatic gear shifting when in forward gears
        if (currentGear > 0)
        {
            // Calculate RPM thresholds for shifting
            float shiftUpRPM = maxRPM * 0.85f;
            float shiftDownRPM = maxRPM * 0.4f;
            
            // Shift up if RPM too high
            if (engineRPM > shiftUpRPM && currentGear < gearRatios.Length)
            {
                currentGear++;
                lastShiftTime = Time.time;
            }
            // Shift down if RPM too low
            else if (engineRPM < shiftDownRPM && currentGear > 1)
            {
                currentGear--;
                lastShiftTime = Time.time;
            }
        }
    }
    
    private void CalculateRPM()
    {
        // Calculate RPM from wheel speed
        float averageWheelRPM = 0f;
        int poweredWheels = 0;
        
        foreach (var wheel in wheels)
        {
            if (wheel.wheelType == WheelType.rear || wheel.wheelType == WheelType.front)
            {
                averageWheelRPM += Mathf.Abs(wheel.collider.rpm);
                poweredWheels++;
            }
        }
        
        if (poweredWheels > 0 && currentGear != 0)
        {
            averageWheelRPM /= poweredWheels;
            
            // Convert wheel RPM to engine RPM based on current gear
            float gearRatio = (currentGear == -1) ? reverseGearRatio : gearRatios[currentGear - 1];
            float targetRPM = averageWheelRPM * gearRatio * finalDriveRatio;
            
            // Idle RPM floor
            targetRPM = Mathf.Max(targetRPM, idleRPM);
            
            // Smooth RPM changes
            engineRPM = Mathf.Lerp(engineRPM, targetRPM, Time.fixedDeltaTime * 5f);
            
            // Add a small RPM boost when accelerating
            if (moveInput.y > 0.1f)
            {
                engineRPM += moveInput.y * 100f;
            }
            
            // Clamp RPM to avoid exceeding redline
            engineRPM = Mathf.Clamp(engineRPM, idleRPM, maxRPM * 1.1f);
        }
        else
        {
            // Engine idle
            engineRPM = Mathf.Lerp(engineRPM, idleRPM, Time.fixedDeltaTime * 3f);
        }
    }
    
    private void CalculateSpeed()
    {
        // Calculate speed in the direction the car is facing
        currentSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        speedKmh = Mathf.Abs(currentSpeed * 3.6f);
        
        // Trigger the UI update event
        OnCarStatsUpdated?.Invoke(speedKmh, engineRPM, currentGear);
    }
    
    private void UpdateWheelVisuals()
    {
        for (int i = 0; i < wheels.Length; i++) 
        {
            Quaternion rot;
            Vector3 pos;
            wheels[i].collider.GetWorldPose(out pos, out rot);

            // Find all child transforms of the wheel collider
            Transform[] childTransforms = new Transform[wheels[i].collider.transform.childCount];
            int index = 0;
            foreach (Transform child in wheels[i].collider.transform)
            {
                // Update position and rotation of visual wheel meshes
                wheels[i].collider.transform.GetChild(index).position = pos;
                wheels[i].collider.transform.GetChild(index).rotation = rot;
                index++;
            }
        }
    }
}

[System.Serializable]
public class Wheel {
    public WheelCollider collider;
    public WheelType wheelType;
}

[System.Serializable]
public enum WheelType {
    front, rear
}