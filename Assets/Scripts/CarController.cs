using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class CarController : MonoBehaviour 
{
    public Wheel[] wheels;
    public Vector2 moveInput;
    public float maxSteer = 30, wheelbase = 2.5f, trackwidth = 1.5f;
    public string playerId { get; set; }
    public bool isLocalPlayer { get; set; }
    
    // Engine properties
    [Header("Engine Settings")]
    [SerializeField] private float maxTorque = 3000f;
    [SerializeField] private float maxRPM = 7000f;
    [SerializeField] private float idleRPM = 800f;
    [SerializeField] private float[] gearRatios = { 3.5f, 2.5f, 1.8f, 1.3f, 1.0f }; // Gear ratios
    [SerializeField] private float[] gearSpeeds; // Max speed for each gear
    [SerializeField] private float finalDriveRatio = 3.5f;
    [SerializeField] private float reverseGearRatio = 3.0f;
    [SerializeField] private float engineBrakeTorque = 500f;
    [SerializeField] private float shiftUpRPM = 6500f;
    [SerializeField] private float shiftDownRPM = 3000f;
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
    public int currentGear { get; private set; } // 0 = neutral, -1 = reverse, 1+ = forward gears
    public bool isReversing { get; private set; }

    // Driving physics
    [Header("Driving Physics")]
    [SerializeField] private float maxSpeed = 200f;
    [SerializeField] private float steeringResponseSpeed = 2f;
    [SerializeField] private float speedSteeringFactor = 0.5f;
    [SerializeField] private float driftFactor = 0.7f;
    [SerializeField] private float downforce = 10f;
    [SerializeField] private float brakeForce = 5000f;
    [SerializeField] private float reverseSpeedLimit = 30f;
    [SerializeField] private float brakeToReverseThreshold = 1f; // Speed below which brake becomes reverse

    [Header("Advanced Handling")]
    public float corneringGrip = 1.0f;          // Higher values improve grip during cornering
    public float oversteerFactor = 0.3f;        // Higher values make the car more prone to oversteer
    public float driftRecoveryFactor = 0.7f;    // Higher values make it recover from drift faster
    public float driftAngleThreshold = 5.0f;    // Angle in degrees before drift kicks in
    public bool enableDriftControl = true;      // Allow player to control drifts with steering
    public float maxBrakeBias = 0.7f;           // 0 = front brakes only, 1 = rear brakes only
    public float tireGripFactor = 1.0f;         // Overall tire grip factor
    public float lateralStiffness = 1.0f;       // Horizontal stiffness of suspension
    
    // Drift state
    private bool isDrifting = false;
    private float driftAngle = 0f;
    private float lateralSlip = 0f;
    
    // Previous frame data for calculations
    private Vector3 prevVelocity;
    private Vector3 prevPosition;

    // Internal variables
    private Rigidbody rb;
    private float currentSteerAngle;
    private float tractionControl = 1f;
    private float wheelCircumference;
    private bool wasBraking;
    private float lastShiftTime;
    private float shiftDelay = 0.5f;
    
    [Header("Audio Settings")]
    public float maxSoundDistance = 30f;
    public float minSoundDistance = 5f;
    public float dopplerLevel = 1f;
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

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
        
        rb.linearDamping = 0.1f;
        rb.angularDamping = 2f;
        CalculateGearSpeeds();
        engineRPM = idleRPM;
        currentGear = 0; // Start in neutral
        
        // Audio setup
        engineAudioSource = GetComponent<AudioSource>();
        if (engineAudioSource == null)
        {
            engineAudioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // Configure spatial audio settings
        engineAudioSource.spatialBlend = 1f; // Full 3D sound
        engineAudioSource.rolloffMode = rolloffMode;
        engineAudioSource.minDistance = minSoundDistance;
        engineAudioSource.maxDistance = maxSoundDistance;
        engineAudioSource.dopplerLevel = dopplerLevel;
        engineAudioSource.loop = true;
        engineAudioSource.clip = engineRunningClip;
        engineAudioSource.Play();
    
        // Make sure we have an AudioListener in the scene
        EnsureAudioListenerExists();
    }

    private void EnsureAudioListenerExists()
    {
        // Find main camera or create one if none exists
        if (Camera.main == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            cameraObj.AddComponent<Camera>();
            cameraObj.tag = "MainCamera";
        }
    
        // Add AudioListener if none exists
        if (Camera.main.GetComponent<AudioListener>() == null)
        {
            Camera.main.gameObject.AddComponent<AudioListener>();
        }
    }
    
    
    
    private void CalculateGearSpeeds()
    {
        gearSpeeds = new float[gearRatios.Length];
        for (int i = 0; i < gearRatios.Length; i++)
        {
            // Theoretical max speed for each gear in km/h
            gearSpeeds[i] = (maxRPM * wheelCircumference * 3.6f) / 
                            (gearRatios[i] * finalDriveRatio * 60f);
        }
    }
    
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
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
    
        // Update spatial position (automatically handles 3D audio)
        engineAudioSource.transform.position = transform.position;
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
        
        // For remote players, disable additional components that might interfere with inputs
        if (!enabled)
        {
            // Disable any input-related components
            var inputComponents = GetComponentsInChildren<MonoBehaviour>();
            foreach (var component in inputComponents)
            {
                if (component.GetType().Name.Contains("Input") && component != this)
                {
                    component.enabled = false;
                }
            }
            
            // Set a layer that ignores raycasts to prevent UI interaction
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
    }

    void FixedUpdate() 
    {
        CalculateSpeed();
        ApplyDownforce();
        ApplyTractionControl();
        HandleGearChanges();
        CalculateRPM();
        
        // Apply driving forces based on current gear state
        if (currentGear > 0) // Forward gears
        {
            HandleForwardDrive();
        }
        else if (currentGear == -1) // Reverse
        {
            HandleReverseDrive();
        }
        else // Neutral
        {
            HandleNeutral();
        }
        
        // Steering (works the same in all gears)
        HandleSteering();
        
        UpdateWheelVisuals();
        DriftPhysics();
    }
    
    private void HandleForwardDrive()
    {
        // Accelerating
        if (moveInput.y > 0)
        {
            float throttle = moveInput.y;
            float torque = CalculateTorque() * throttle;
            
            // Apply torque to powered wheels
            foreach (var wheel in wheels) 
            {
                if (wheel.wheelType == WheelType.rear || wheel.wheelType == WheelType.front)
                {
                    wheel.collider.brakeTorque = 0;
                    wheel.collider.motorTorque = torque / (wheels.Length / 2f);
                }
            }
            
            // Apply small amount of brake to non-powered wheels for stability
            foreach (var wheel in wheels)
            {
                if (!(wheel.wheelType == WheelType.rear || wheel.wheelType == WheelType.front))
                {
                    wheel.collider.brakeTorque = engineBrakeTorque * 0.1f;
                }
            }
        }
        // Braking/coasting
        else
        {
            if (moveInput.y < 0 || speedKmh > gearSpeeds[currentGear - 1] * 1.1f)
            {
                // Full brake
                float brake = (moveInput.y < 0) ? -moveInput.y * brakeForce : brakeForce * 0.3f;
                ApplyBrakes(brake);
                
                // If nearly stopped while braking, prepare for potential reverse
                if (speedKmh < brakeToReverseThreshold && moveInput.y < 0)
                {
                    wasBraking = true;
                }
            }
            else
            {
                // Engine braking when off throttle
                ApplyBrakes(engineBrakeTorque * (engineRPM / maxRPM) * 0.7f);
            }
        }
    }
    
    private void HandleReverseDrive()
    {
        // Accelerating in reverse
        if (moveInput.y < 0)
        {
            float throttle = -moveInput.y;
            float torque = CalculateTorque() * throttle * 0.6f; // Reduced power in reverse
            
            foreach (var wheel in wheels) 
            {
                if (wheel.wheelType == WheelType.rear || wheel.wheelType == WheelType.front)
                {
                    wheel.collider.brakeTorque = 0;
                    wheel.collider.motorTorque = -torque / (wheels.Length / 2f); // Negative torque for reverse
                }
            }
            
            // Limit reverse speed
            if (speedKmh > reverseSpeedLimit)
            {
                ApplyBrakes(brakeForce * 0.5f);
            }
        }
        // Braking (which becomes forward acceleration when stopped)
        else
        {
            if (moveInput.y > 0 || speedKmh > 5f)
            {
                // Brake in reverse
                float brake = (moveInput.y > 0) ? moveInput.y * brakeForce : brakeForce * 0.3f;
                ApplyBrakes(brake);
                
                // If nearly stopped while pressing forward, prepare to shift to drive
                if (speedKmh < brakeToReverseThreshold && moveInput.y > 0)
                {
                    wasBraking = true;
                }
            }
            else
            {
                // Engine braking when off throttle in reverse
                ApplyBrakes(engineBrakeTorque * (engineRPM / maxRPM) * 0.5f);
            }
        }
    }
    
    private void HandleNeutral()
    {
        // In neutral, just apply brakes based on input
        if (Mathf.Abs(moveInput.y) > 0.1f)
        {
            ApplyBrakes(Mathf.Abs(moveInput.y) * brakeForce * 0.7f);
        }
        else
        {
            ApplyBrakes(engineBrakeTorque * 0.2f);
        }
        
        // If getting input in neutral and nearly stopped, prepare to shift
        if (speedKmh < brakeToReverseThreshold && Mathf.Abs(moveInput.y) > 0.1f)
        {
            wasBraking = true;
        }
    }
    
    private void ApplyBrakes(float brakeAmount)
    {
        foreach (var wheel in wheels)
        {
            wheel.collider.brakeTorque = brakeAmount;
            wheel.collider.motorTorque = 0;
        }
    }
    
    private void HandleGearChanges()
    {
        // Don't shift too frequently
        if (Time.time - lastShiftTime < shiftDelay) return;
        
        // If we were braking and now have input, change gear appropriately
        if (wasBraking && Mathf.Abs(moveInput.y) > 0.1f)
        {
            if (moveInput.y > 0 && speedKmh < brakeToReverseThreshold)
            {
                // Shift to first gear if pressing forward
                currentGear = 1;
                isReversing = false;
                lastShiftTime = Time.time;
            }
            else if (moveInput.y < 0 && speedKmh < brakeToReverseThreshold)
            {
                // Shift to reverse if pressing backward
                currentGear = -1;
                isReversing = true;
                lastShiftTime = Time.time;
            }
            wasBraking = false;
        }
        
        // Automatic gear shifting when in forward gears
        if (currentGear > 0)
        {
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
        
        // Automatic shift to neutral when stopped
        if (speedKmh < 0.5f && Mathf.Abs(moveInput.y) < 0.1f)
        {
            currentGear = 0;
            isReversing = false;
        }
    }
    
    private void CalculateRPM()
    {
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
            float newRPM = averageWheelRPM * gearRatio * finalDriveRatio;
            
            // Smooth RPM changes
            engineRPM = Mathf.Lerp(engineRPM, Mathf.Clamp(newRPM, idleRPM * 0.8f, maxRPM * 1.1f), Time.fixedDeltaTime * 5f);
        }
        else
        {
            // Engine idle when in neutral or no powered wheels
            engineRPM = Mathf.Lerp(engineRPM, idleRPM, Time.fixedDeltaTime * 2f);
        }
    }
    
    private float CalculateTorque()
    {
        // Simple torque curve - peaks around mid-RPM range
        float rpmNormalized = Mathf.Clamp01((engineRPM - idleRPM) / (maxRPM - idleRPM));
        float torqueCurve = Mathf.Sin(rpmNormalized * Mathf.PI * 0.7f); // Peaks around 70% of RPM range
        return Mathf.Lerp(maxTorque * 0.3f, maxTorque, torqueCurve) * tractionControl;
    }
    
    private void HandleSteering()
    {
        float speedFactor = 1f - (Mathf.Clamp01(speedKmh / maxSpeed) * speedSteeringFactor);
        float targetSteer = moveInput.x * maxSteer * speedFactor;
        
        // Make steering more responsive at lower speeds
        float speedBasedResponse = Mathf.Lerp(steeringResponseSpeed * 1.5f, steeringResponseSpeed, speedKmh / 50f);
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteer, speedBasedResponse * Time.fixedDeltaTime);
        
        // Improved Ackermann steering that works for both wheels equally
        if (Mathf.Abs(moveInput.x) > 0.01f) {
            // Calculate inner and outer wheel angles
            float innerWheelAngle, outerWheelAngle;
            
            if (moveInput.x > 0) { // Turning right
                // Right wheel is the inner wheel
                innerWheelAngle = currentSteerAngle;
                // Calculate the left wheel angle (outer) using Ackermann
                outerWheelAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (trackwidth + Mathf.Tan(Mathf.Deg2Rad * innerWheelAngle) * wheelbase));
                
                // Left wheel (index 0) gets outer angle, right wheel (index 1) gets inner angle
                wheels[0].collider.steerAngle = outerWheelAngle;
                wheels[1].collider.steerAngle = innerWheelAngle;
            } 
            else { // Turning left
                // Left wheel is the inner wheel
                innerWheelAngle = currentSteerAngle;
                // Calculate the right wheel angle (outer) using Ackermann
                outerWheelAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (trackwidth + Mathf.Tan(Mathf.Deg2Rad * Mathf.Abs(innerWheelAngle)) * wheelbase));
                
                // Left wheel (index 0) gets inner angle, right wheel (index 1) gets outer angle
                wheels[0].collider.steerAngle = innerWheelAngle;
                wheels[1].collider.steerAngle = -outerWheelAngle; // Negative because we're turning left
            }
        } 
        else {
            // No steering input, set both wheels straight
            wheels[0].collider.steerAngle = wheels[1].collider.steerAngle = 0;
        }
    }
    
    private void ApplyDownforce()
    {
        rb.AddForce(-transform.up * downforce * rb.linearVelocity.sqrMagnitude * 0.001f);
    }
    
    private void ApplyTractionControl()
    {
        float wheelSlip = 0f;
        int poweredWheels = 0;
        
        foreach (var wheel in wheels)
        {
            if (wheel.wheelType == WheelType.rear || wheel.wheelType == WheelType.front)
            {
                WheelHit hit;
                if (wheel.collider.GetGroundHit(out hit))
                {
                    wheelSlip += hit.forwardSlip;
                    poweredWheels++;
                }
            }
        }
        
        if (poweredWheels > 0)
        {
            wheelSlip /= poweredWheels;
            tractionControl = Mathf.Lerp(tractionControl, 1f - Mathf.Clamp01(Mathf.Abs(wheelSlip)), Time.fixedDeltaTime * 5f);
        }
        else
        {
            tractionControl = 1f;
        }
    }
    
    private void CalculateSpeed()
    {
        currentSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        speedKmh = Mathf.Abs(currentSpeed * 3.6f);
    }
    
    private void UpdateWheelVisuals()
    {
        foreach (var wheel in wheels) 
        {
            Quaternion rot;
            Vector3 pos;
            wheel.collider.GetWorldPose(out pos, out rot);

            foreach (Transform child in wheel.collider.transform)
            {
                child.position = pos;
                child.rotation = rot;
            }
        }
    }
    
    private void DriftPhysics()
    {
        // Calculate lateral slip (sideways movement)
        float sidewaysSpeed = Vector3.Dot(rb.velocity, transform.right);
        lateralSlip = Mathf.Abs(sidewaysSpeed);
        
        // Calculate angle between velocity and forward direction
        float carAngle = 0;
        if (rb.velocity.magnitude > 2f) // Only calculate when moving
        {
            carAngle = Vector3.Angle(transform.forward, rb.velocity);
            float dir = Mathf.Sign(Vector3.Dot(transform.right, rb.velocity));
            carAngle *= dir; // Negative when sliding left, positive when sliding right
        }
        
        // Determine if we're drifting based on angle threshold
        isDrifting = Mathf.Abs(carAngle) > driftAngleThreshold && speedKmh > 25f;
        driftAngle = carAngle;
        
        // Apply drift forces
        if (speedKmh > 15f)
        {
            // Base grip force that counters sideways movement
            float baseGripFactor = tireGripFactor * (1f - (Mathf.Abs(moveInput.x) * 0.3f));
            float gripForce = baseGripFactor;
            
            // Reduce grip while drifting
            if (isDrifting)
            {
                // Less grip during drift, especially when steering into the drift
                float steeringIntoSlide = Mathf.Sign(moveInput.x) == Mathf.Sign(sidewaysSpeed);
                gripForce *= steeringIntoSlide ? 0.6f : 0.8f;
                
                // Add some rotation based on steering input if drift control is enabled
                if (enableDriftControl && Mathf.Abs(moveInput.x) > 0.2f)
                {
                    // This lets the player control the car's rotation while drifting
                    rb.AddTorque(transform.up * moveInput.x * oversteerFactor * 
                                  (speedKmh * 0.01f), ForceMode.Acceleration);
                }
            }
            else
            {
                // Normal cornering - increase grip during cornering
                gripForce *= 1f + (corneringGrip * Mathf.Abs(moveInput.x));
            }
            
            // Apply counterforce to sideways movement based on grip
            Vector3 driftForce = -transform.right * (sidewaysSpeed * driftFactor * gripForce);
            rb.AddForce(driftForce, ForceMode.Acceleration);
            
            // Apply recovery torque to help straighten out after drifts
            if (Mathf.Abs(carAngle) > 5f && Mathf.Abs(moveInput.x) < 0.3f)
            {
                float recoveryTorque = -Mathf.Sign(carAngle) * driftRecoveryFactor * Mathf.Min(30f, Mathf.Abs(carAngle));
                rb.AddTorque(transform.up * recoveryTorque, ForceMode.Acceleration);
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