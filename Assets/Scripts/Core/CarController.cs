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
    
    [Header("Steering Settings")]
    public float ackermannFactor = 0.85f; // How much the outer wheel turns relative to inner wheel (0.8-1.0)
    public float centeringDeadzone = 0.1f; // Angle below which wheels snap to center
    public float steeringInputDeadzone = 0.01f; // Input threshold for steering
    
    // Engine properties
    [Header("Engine Settings")]
    [SerializeField] private float maxRPM = 8800f;
    [SerializeField] private float idleRPM = 800f;
    [SerializeField] private float[] gearRatios = { 3.82f, 2.26f, 1.64f, 1.29f, 1.06f, 0.84f, 0.62f };
    [SerializeField] private float finalDriveRatio = 3.44f;
    [SerializeField] private float reverseGearRatio = 3.67f;
    
    [Header("Transmission Settings")]
    public bool useManualTransmission = true; // Toggle between manual and automatic
    public bool allowReverseFromNeutral = true; // Allow shifting to reverse when stopped
    
    [Header("Engine Sounds")]
    public AudioClip engineIdleClip;
    public AudioClip engineRunningClip;
    public AudioClip gearShiftClip; // Add gear shift sound
    public float minPitch = 0.7f;
    public float maxPitch = 1.5f;
    public float pitchMultiplier = 1f;
    private AudioSource engineAudioSource;
    private AudioSource gearShiftAudioSource; // Separate audio source for gear shifts
    
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
        
        // Setup gear shift audio source
        gearShiftAudioSource = gameObject.AddComponent<AudioSource>();
        gearShiftAudioSource.spatialBlend = 1f;
        gearShiftAudioSource.rolloffMode = AudioRolloffMode.Linear;
        gearShiftAudioSource.minDistance = 3f;
        gearShiftAudioSource.maxDistance = 15f;
        gearShiftAudioSource.volume = 0.7f;
    }
    
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }
    
    // Manual gear shifting input handlers
    public void OnShiftUp(InputValue value)
    {
        if (value.isPressed && useManualTransmission)
        {
            ShiftUp();
        }
    }
    
    public void OnShiftDown(InputValue value)
    {
        if (value.isPressed && useManualTransmission)
        {
            ShiftDown();
        }
    }

    public void EnableControls(bool enabled)
    {
        if (!enabled)
        {
            moveInput = Vector2.zero;
        }
        
        var playerInputComponent = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInputComponent != null)
        {
            playerInputComponent.enabled = enabled;
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
        
        // In neutral gear, no power transmission
        if (currentGear == 0)
        {
            foreach (var wheel in wheels)
            {
                wheel.collider.motorTorque = 0;
                // Light braking for natural slowdown
                wheel.collider.brakeTorque = throttle < 0 ? -throttle * brakeForce * 0.1f : 10f;
            }
            return;
        }
        
        if (throttle > 0) // Accelerating
        {
            // Only apply power if we're in a valid gear
            if (currentGear > 0 || currentGear == -1)
            {
                // Apply driving force to appropriate wheels
                foreach (var wheel in wheels)
                {
                    // Handle FWD, RWD, or AWD configurations
                    if ((wheel.wheelType == WheelType.front) || (wheel.wheelType == WheelType.rear))
                    {
                        float powerOutput = throttle * powerMultiplier / 2.0f; // Divide power between wheels
                        
                        // Reverse the power direction if in reverse gear
                        if (currentGear == -1)
                        {
                            powerOutput *= -1;
                        }
                        
                        wheel.collider.motorTorque = powerOutput;
                        wheel.collider.brakeTorque = 0; // Release brakes when accelerating
                    }
                }
            }
        }
        else if (throttle < 0) // Braking
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
        }
        else // No input - let the car coast
        {
            foreach (var wheel in wheels)
            {
                wheel.collider.motorTorque = 0;
                wheel.collider.brakeTorque = 10f; // Light braking for natural slowdown
            }
        }
    }
    
    private void HandleSmoothSteering()
    {
        // Calculate target steering angle based on input
        float targetSteerAngle = moveInput.x * maxSteer;
        
        // Smoothly interpolate current steering angle toward target
        if (Mathf.Abs(moveInput.x) > steeringInputDeadzone)
        {
            // Moving toward the target steering angle
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, Time.fixedDeltaTime * steeringSpeed);
        }
        else
        {
            // Returning to center faster when no input
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, 0, Time.fixedDeltaTime * returnToZeroSpeed);
        }
        
        // Apply simplified Ackermann steering geometry
        // For most cars, the difference between inner and outer wheel angles is small
        // We'll use a simplified approach that's more stable
        
        if (Mathf.Abs(currentSteerAngle) < centeringDeadzone)
        {
            // When steering angle is very small, set both wheels to zero
            // This ensures proper return to center
            foreach (var wheel in wheels)
            {
                if (wheel.wheelType == WheelType.front)
                {
                    wheel.collider.steerAngle = 0f;
                }
            }
        }
        else
        {
            // Apply Ackermann steering with simplified calculation
            float innerWheelAngle = currentSteerAngle;
            float outerWheelAngle = currentSteerAngle * ackermannFactor; // Outer wheel turns less (simplified Ackermann)
            
            if (currentSteerAngle > 0) // Turning right
            {
                // Left wheel (outer) gets reduced angle
                wheels[0].collider.steerAngle = outerWheelAngle;
                // Right wheel (inner) gets full angle
                wheels[1].collider.steerAngle = innerWheelAngle;
            }
            else // Turning left
            {
                // Left wheel (inner) gets full angle
                wheels[0].collider.steerAngle = innerWheelAngle;
                // Right wheel (outer) gets reduced angle
                wheels[1].collider.steerAngle = outerWheelAngle;
            }
        }
    }
    
    private void HandleGearChanges()
    {
        // Only use automatic shifting if manual transmission is disabled
        if (!useManualTransmission)
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
    }
    
    // Manual transmission methods
    public void ShiftUp()
    {
        // Prevent rapid shifting
        if (Time.time - lastShiftTime < shiftDelay) return;
        
        if (currentGear == -1) // From reverse to neutral (0) to first (1)
        {
            if (speedKmh < 5f) // Only shift out of reverse when nearly stopped
            {
                currentGear = 0; // Neutral
                lastShiftTime = Time.time;
                PlayGearShiftSound();
                Debug.Log("Shifted to Neutral");
            }
        }
        else if (currentGear == 0) // From neutral to first gear
        {
            currentGear = 1;
            lastShiftTime = Time.time;
            PlayGearShiftSound();
            Debug.Log("Shifted to 1st gear");
        }
        else if (currentGear > 0 && currentGear < gearRatios.Length) // Normal upshift
        {
            currentGear++;
            lastShiftTime = Time.time;
            PlayGearShiftSound();
            Debug.Log($"Shifted up to gear {currentGear}");
        }
    }
    
    public void ShiftDown()
    {
        // Prevent rapid shifting
        if (Time.time - lastShiftTime < shiftDelay) return;
        
        if (currentGear > 1) // Normal downshift
        {
            currentGear--;
            lastShiftTime = Time.time;
            PlayGearShiftSound();
            Debug.Log($"Shifted down to gear {currentGear}");
        }
        else if (currentGear == 1) // From first to neutral
        {
            currentGear = 0;
            lastShiftTime = Time.time;
            PlayGearShiftSound();
            Debug.Log("Shifted to Neutral");
        }
        else if (currentGear == 0 && allowReverseFromNeutral) // From neutral to reverse
        {
            if (speedKmh < 5f) // Only shift to reverse when nearly stopped
            {
                currentGear = -1;
                lastShiftTime = Time.time;
                PlayGearShiftSound();
                Debug.Log("Shifted to Reverse");
            }
        }
    }
    
    private void PlayGearShiftSound()
    {
        if (gearShiftAudioSource != null && gearShiftClip != null)
        {
            gearShiftAudioSource.clip = gearShiftClip;
            gearShiftAudioSource.pitch = Random.Range(0.9f, 1.1f); // Slight pitch variation
            gearShiftAudioSource.Play();
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
        
        // Trigger the UI update event with proper gear display
        int displayGear = currentGear;
        OnCarStatsUpdated?.Invoke(speedKmh, engineRPM, displayGear);
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