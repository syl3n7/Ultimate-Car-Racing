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
    [SerializeField] private float maxTorque = 4500f; // Higher torque for 911 Carrera S (450-500 Nm)
    [SerializeField] private float maxRPM = 8800f; // Authentic 911 Carrera S redline
    [SerializeField] private float idleRPM = 900f; // Slightly higher idle for racing engine
    [SerializeField] private float[] gearRatios = { 3.82f, 2.26f, 1.64f, 1.29f, 1.06f, 0.84f, 0.62f }; // 911's 7-speed PDK gearbox ratios
    [SerializeField] private float[] gearSpeeds; // Max speed for each gear
    [SerializeField] private float finalDriveRatio = 3.44f; // 911 Carrera S final drive
    [SerializeField] private float reverseGearRatio = 3.67f; // Authentic reverse ratio
    [SerializeField] private float engineBrakeTorque = 850f; // Increased engine braking
    [SerializeField] private float shiftUpRPM = 8300f; // Shift near redline for performance
    [SerializeField] private float shiftDownRPM = 3500f; // Keep revs up for better response
    [Header("Engine Sounds")]
    public AudioClip engineIdleClip;
    public AudioClip engineRunningClip;
    public float minPitch = 0.7f;
    public float maxPitch = 1.5f;
    public float pitchMultiplier = 1f;
    private AudioSource engineAudioSource;

    public enum DrivetrainType
    {
        RWD, // Rear-wheel drive (authentic 911)
        FWD, // Front-wheel drive
        AWD  // All-wheel drive
    }

    [Header("Drivetrain Configuration")]
    public DrivetrainType drivetrainType = DrivetrainType.RWD;
    [Range(0f, 1f)]
    public float frontPowerDistribution = 0.35f; // For AWD: percentage of power to front wheels (35/65 for 911 AWD models)

    // Speed properties
    public float currentSpeed { get; private set; }
    public float speedKmh { get; private set; }
    public float engineRPM { get; private set; }
    public int currentGear { get; private set; } // 0 = neutral, -1 = reverse, 1+ = forward gears
    public bool isReversing { get; private set; }
    
    // Event for UI updates
    public delegate void CarStatsUpdated(float speed, float rpm, int gear);
    public event CarStatsUpdated OnCarStatsUpdated;

    // Driving physics
    [Header("Driving Physics")]
    [SerializeField] private float maxSpeed = 220f; // Porsche 911 is fast
    [SerializeField] private float steeringResponseSpeed = 2.2f; // Responsive steering
    [SerializeField] private float speedSteeringFactor = 0.45f; // Slightly more responsive at speed
    [SerializeField] private float driftFactor = 0.8f; // More drift-prone like a 911
    [SerializeField] private float downforce = 12f; // Better downforce with 911 aerodynamics
    [SerializeField] private float brakeForce = 5500f; // Strong brakes
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
            
            // Apply torque based on drivetrain type
            ApplyDrivetrainTorque(torque);
            
            // Apply more torque during cornering to counteract understeer (helps FWD cars turn)
            if (Mathf.Abs(moveInput.x) > 0.2f)
            {
                ApplyCorneringTorque();
            }
        }
        // Braking/coasting
        else
        {
            if (moveInput.y < 0 || speedKmh > gearSpeeds[currentGear - 1] * 1.1f)
            {
                // Full brake - apply more braking to front wheels (realistic weight transfer)
                float brakePower = (moveInput.y < 0) ? -moveInput.y * brakeForce : brakeForce * 0.3f;
                
                // Apply braking with proper front/rear bias
                foreach (var wheel in wheels)
                {
                    if (wheel.wheelType == WheelType.front)
                    {
                        wheel.collider.brakeTorque = brakePower * (1.0f - maxBrakeBias);
                    }
                    else if (wheel.wheelType == WheelType.rear)
                    {
                        wheel.collider.brakeTorque = brakePower * maxBrakeBias;
                    }
                    
                    wheel.collider.motorTorque = 0;
                }
                
                // If nearly stopped while braking, prepare for potential reverse
                if (speedKmh < brakeToReverseThreshold && moveInput.y < 0)
                {
                    wasBraking = true;
                }
            }
            else
            {
                // Engine braking when off throttle
                ApplyEngineBraking();
            }
        }
    }
    
    private void ApplyDrivetrainTorque(float totalTorque)
    {
        int frontPoweredWheels = 0;
        int rearPoweredWheels = 0;
        
        // Count powered wheels by type
        foreach (var wheel in wheels)
        {
            if (wheel.wheelType == WheelType.front && (drivetrainType == DrivetrainType.FWD || drivetrainType == DrivetrainType.AWD))
                frontPoweredWheels++;
            else if (wheel.wheelType == WheelType.rear && (drivetrainType == DrivetrainType.RWD || drivetrainType == DrivetrainType.AWD))
                rearPoweredWheels++;
        }
        
        // Calculate torque per wheel based on drivetrain type
        float frontWheelTorque = 0f;
        float rearWheelTorque = 0f;
        
        switch (drivetrainType)
        {
            case DrivetrainType.FWD:
                frontWheelTorque = frontPoweredWheels > 0 ? totalTorque / frontPoweredWheels : 0;
                break;
            case DrivetrainType.RWD:
                rearWheelTorque = rearPoweredWheels > 0 ? totalTorque / rearPoweredWheels : 0;
                break;
            case DrivetrainType.AWD:
                // Distribute torque according to front/rear power distribution
                float frontTorque = totalTorque * frontPowerDistribution;
                float rearTorque = totalTorque * (1 - frontPowerDistribution);
                
                frontWheelTorque = frontPoweredWheels > 0 ? frontTorque / frontPoweredWheels : 0;
                rearWheelTorque = rearPoweredWheels > 0 ? rearTorque / rearPoweredWheels : 0;
                break;
        }
        
        // Apply calculated torque to wheels
        foreach (var wheel in wheels)
        {
            wheel.collider.brakeTorque = 0;
            
            if (wheel.wheelType == WheelType.front)
                wheel.collider.motorTorque = frontWheelTorque;
            else if (wheel.wheelType == WheelType.rear)
                wheel.collider.motorTorque = rearWheelTorque;
        }
    }
    
    private void ApplyCorneringTorque()
    {
        // Apply more torque to outer wheels during cornering to help turn
        // Works best for FWD cars but also helps with AWD
        if (drivetrainType == DrivetrainType.FWD || drivetrainType == DrivetrainType.AWD)
        {
            foreach (var wheel in wheels)
            {
                if (wheel.wheelType == WheelType.front)
                {
                    // Add extra torque to the outer wheel to help cornering
                    // Assuming wheels[0] is left front and wheels[1] is right front
                    if ((moveInput.x > 0 && wheel == wheels[0]) || (moveInput.x < 0 && wheel == wheels[1]))
                    {
                        wheel.collider.motorTorque *= 1.2f; // More power to outer wheel
                    }
                }
            }
        }
    }
    
    private void ApplyEngineBraking()
    {
        float engineBrake = engineBrakeTorque * (engineRPM / maxRPM) * 0.7f;
        
        // Apply engine braking according to drivetrain type
        foreach (var wheel in wheels)
        {
            switch (drivetrainType)
            {
                case DrivetrainType.FWD:
                    if (wheel.wheelType == WheelType.front)
                        wheel.collider.brakeTorque = engineBrake;
                    else
                        wheel.collider.brakeTorque = engineBrake * 0.3f;
                    break;
                    
                case DrivetrainType.RWD:
                    if (wheel.wheelType == WheelType.rear)
                        wheel.collider.brakeTorque = engineBrake;
                    else
                        wheel.collider.brakeTorque = engineBrake * 0.3f;
                    break;
                    
                case DrivetrainType.AWD:
                    // More balanced engine braking for AWD
                    wheel.collider.brakeTorque = engineBrake * 0.5f;
                    break;
            }
            
            wheel.collider.motorTorque = 0;
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
        // More gradual speed factor to prevent extreme steering at high speeds but still allow good turning
        float speedFactor = 1f - (Mathf.Clamp01(speedKmh / (maxSpeed * 0.8f)) * speedSteeringFactor * 0.8f);
        
        // Apply grip factor based on acceleration state - 911s have sharper turn-in but can understeer with throttle
        float accelerationFactor = moveInput.y > 0.5f ? 0.9f : (moveInput.y < -0.1f ? 1.1f : 1.0f);
        float gripAdjustedSteer = maxSteer * speedFactor * accelerationFactor;
        
        // Calculate target steering angle with more stability
        float targetSteer = moveInput.x * gripAdjustedSteer;
        
        // Slower steering response at high speeds, more responsive at low speeds
        float speedBasedResponse = Mathf.Lerp(steeringResponseSpeed * 1.2f, steeringResponseSpeed * 0.8f, speedKmh / 60f);
        
        // Fast steering response for keyboard controls (GTA V-like)
        if (Mathf.Abs(moveInput.x) > 0.1f) {
            // When actively steering, respond very quickly
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteer, 0.5f); // Very fast response with keyboard
        } else {
            // When not steering, also return to center more gradually but still quicker than before
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, 0, speedBasedResponse * Time.fixedDeltaTime * 120f);
        }
        
        // Apply Ackermann steering with improved stability and accuracy
        if (Mathf.Abs(moveInput.x) > 0.01f) {
            // Calculate inner and outer wheel angles with reduced sensitivity
            float steeringAngle = currentSteerAngle * 0.9f; // Slightly reduce actual steering for stability
            float innerWheelAngle, outerWheelAngle;
            
            if (moveInput.x > 0) { // Turning right
                // Right wheel is the inner wheel
                innerWheelAngle = steeringAngle;
                // Calculate the left wheel angle (outer) using Ackermann with enhanced stability
                outerWheelAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (trackwidth + Mathf.Tan(Mathf.Deg2Rad * innerWheelAngle) * wheelbase));
                
                // Left wheel (index 0) gets outer angle, right wheel (index 1) gets inner angle
                wheels[0].collider.steerAngle = outerWheelAngle;
                wheels[1].collider.steerAngle = innerWheelAngle;
            } 
            else { // Turning left
                // Left wheel is the inner wheel
                innerWheelAngle = steeringAngle;
                // Calculate the right wheel angle (outer) using Ackermann
                outerWheelAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (trackwidth + Mathf.Tan(Mathf.Deg2Rad * Mathf.Abs(innerWheelAngle)) * wheelbase));
                
                // Left wheel (index 0) gets inner angle, right wheel (index 1) gets outer angle
                wheels[0].collider.steerAngle = innerWheelAngle;
                wheels[1].collider.steerAngle = -outerWheelAngle; // Negative because we're turning left
            }
        } 
        else {
            // No steering input, quickly return wheels to center (GTA V-like keyboard responsiveness)
            float returnSpeed = Mathf.Lerp(30f, 20f, speedKmh / 100f); // Much faster return for keyboard controls
            wheels[0].collider.steerAngle = Mathf.MoveTowards(wheels[0].collider.steerAngle, 0, returnSpeed * Time.fixedDeltaTime * 60f);
            wheels[1].collider.steerAngle = Mathf.MoveTowards(wheels[1].collider.steerAngle, 0, returnSpeed * Time.fixedDeltaTime * 60f);
        }
    }
    
    private void ApplyDownforce()
    {
        // Apply progressive downforce that increases with speed (like a 911's aerodynamics)
        float speedFactor = Mathf.Clamp01(speedKmh / maxSpeed);
        float progressiveDownforce = downforce * (1 + speedFactor);
        
        // Apply more downforce to rear of car (where 911's engine is)
        rb.AddForceAtPosition(-transform.up * progressiveDownforce * rb.linearVelocity.sqrMagnitude * 0.001f, 
            transform.position - transform.forward * 0.5f, ForceMode.Acceleration);
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
        
        // Trigger the UI update event
        OnCarStatsUpdated?.Invoke(speedKmh, engineRPM, currentGear);
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
        float sidewaysSpeed = Vector3.Dot(rb.linearVelocity, transform.right);
        lateralSlip = Mathf.Abs(sidewaysSpeed);
        
        // Calculate angle between velocity and forward direction
        float carAngle = 0;
        if (rb.linearVelocity.magnitude > 2f) // Only calculate when moving
        {
            carAngle = Vector3.Angle(transform.forward, rb.linearVelocity);
            float dir = Mathf.Sign(Vector3.Dot(transform.right, rb.linearVelocity));
            carAngle *= dir; // Negative when sliding left, positive when sliding right
        }
        
        // Use a higher threshold for drift detection and add speed dependency
        float adjustedThreshold = driftAngleThreshold * (1f + (speedKmh / 100f));
        
        // Determine if we're drifting based on adjusted angle threshold
        isDrifting = Mathf.Abs(carAngle) > adjustedThreshold && speedKmh > 30f;
        driftAngle = carAngle;
        
        // Apply drift forces with more stability
        if (speedKmh > 10f)
        {
            // Calculate front/rear weight distribution based on acceleration state
            float frontWeight = 0.5f + (moveInput.y < 0 ? 0.1f : (moveInput.y > 0 ? -0.05f : 0));
            
            // Porsche 911 has more weight on the rear, especially when accelerating
            float rearWheelGrip = 1.1f + (Mathf.Abs(moveInput.y) * 0.3f); // More grip on rear wheels with acceleration
            
            // Base grip factor that counters sideways movement - tuned for 911 handling
            float baseGripFactor = tireGripFactor * 1.0f;
            
            // Apply rear-wheel drive characteristics - decrease grip under hard acceleration (prone to wheelspin)
            if (moveInput.y > 0.7f) {
                baseGripFactor *= 0.9f; // Slight loss of grip when flooring it (Porsche 911 wheelspin)
            }
            
            float gripForce = baseGripFactor;
            
            // Reduce grip while drifting, but not as drastically
            if (isDrifting)
            {
                // Less grip during drift, but maintain more front grip for FWD
                bool isSameDirection = Mathf.Sign(moveInput.x) == Mathf.Sign(sidewaysSpeed);
                gripForce *= isSameDirection ? 0.8f : 0.9f;
                
                // Add some rotation based on steering input if drift control is enabled
                if (enableDriftControl && Mathf.Abs(moveInput.x) > 0.2f)
                {
                    // This lets the player control the car's rotation while drifting (reduced effect)
                    rb.AddTorque(transform.up * moveInput.x * oversteerFactor * 0.7f * 
                                  (speedKmh * 0.01f), ForceMode.Acceleration);
                }
            }
            else
            {
                // Normal cornering - increase grip during cornering with Porsche 911 characteristics
                // More oversteer in turns, especially during acceleration
                float turnGrip = corneringGrip;
                if (moveInput.y > 0.5f) {
                    // Reduce grip when accelerating in corners (911 oversteer)
                    turnGrip *= 0.85f;
                }
                gripForce *= 1f + (turnGrip * Mathf.Abs(moveInput.x) * 0.08f);
            }
            
            // Apply stronger counterforce to sideways movement for stability
            Vector3 driftForce = -transform.right * (sidewaysSpeed * driftFactor * gripForce * 1.2f);
            rb.AddForce(driftForce, ForceMode.Acceleration);
            
            // More aggressive recovery torque to prevent excessive spinning
            if (Mathf.Abs(carAngle) > 3f)
            {
                // Stronger recovery with less input, less recovery during intentional turning
                float inputReductionFactor = 1f - (Mathf.Abs(moveInput.x) * 0.3f);
                float recoveryTorque = -Mathf.Sign(carAngle) * driftRecoveryFactor * 1.2f * 
                                      inputReductionFactor * Mathf.Min(25f, Mathf.Abs(carAngle));
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