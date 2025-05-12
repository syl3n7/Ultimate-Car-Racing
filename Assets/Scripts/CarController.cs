using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    [Header("Wheels")]
    public Wheel[] wheels;
    
    [Header("Engine")]
    public float motorPower = 1000f;
    public float brakePower = 3000f;
    public AnimationCurve powerCurve;
    
    [Header("Steering")]
    public float maxSteerAngle = 30f;
    public float steerSpeed = 5f;
    public float wheelbase = 2.5f; // Distance between front and rear wheels
    public float trackWidth = 1.5f; // Distance between left and right wheels
    public bool useAckermannSteering = true;
    
    [Header("Stability")]
    public Transform centerOfMass;
    public float downforceCoefficient = 1.0f;
    public float antiRollForce = 5000f;
    
    [Header("Input")]
    public Vector2 moveInput;
    private float currentSteerAngle = 0f;
    private Rigidbody carRigidbody;
    
    // Network properties
    [HideInInspector] public bool IsLocal { get; private set; }
    [HideInInspector] public string PlayerId { get; private set; }
    [HideInInspector] public bool HasInputChanges { get; set; }
    [HideInInspector] public float LastStateTimestamp { get; set; }
    [HideInInspector] public float LastInputTimestamp { get; set; }
    
    public Rigidbody Rigidbody => carRigidbody;
    public float CurrentThrottle => moveInput.y;
    public float CurrentSteering => moveInput.x;
    public float CurrentBrake => (moveInput.y < 0f) ? -moveInput.y : 0f;

    void Awake()
    {
        carRigidbody = GetComponent<Rigidbody>();
        if(centerOfMass != null)
            carRigidbody.centerOfMass = centerOfMass.localPosition;
    }
    
    public void Initialize(string playerId, bool isLocal)
    {
        PlayerId = playerId;
        IsLocal = isLocal;
        enabled = true;
    }
    
    void FixedUpdate()
    {
        if (wheels == null || wheels.Length == 0)
        {
            Debug.LogWarning("No wheels assigned to car controller");
            return;
        }
        
        // Add debugging output
        Debug.Log($"Input: {moveInput}, Speed: {carRigidbody.velocity.magnitude} m/s");
        
        ApplyMotorTorque();
        ApplySteering();
        UpdateWheelVisuals();
        ApplyDownforce();
        ApplyAntiRoll();
    }
    
    void ApplyMotorTorque()
    {
        float throttleInput = moveInput.y;
        float brakeInput = (throttleInput < 0) ? -throttleInput : 0;
        
        // Get the engine RPM (average of powered wheels)
        float rpm = 0;
        int poweredWheels = 0;
        
        foreach (var wheel in wheels)
        {
            if (wheel.powered)
            {
                rpm += wheel.collider.rpm;
                poweredWheels++;
            }
        }
        
        if (poweredWheels > 0)
            rpm /= poweredWheels;
            
        // Normalize RPM for power curve (0-1 range)
        float normalizedRPM = Mathf.Clamp01(Mathf.Abs(rpm) / 3000f);
        float powerFactor = powerCurve.Evaluate(normalizedRPM);
        
        foreach (var wheel in wheels)
        {
            // Apply motor torque to powered wheels
            if (wheel.powered && throttleInput > 0)
            {
                wheel.collider.motorTorque = throttleInput * motorPower * powerFactor;
            }
            else
            {
                wheel.collider.motorTorque = 0;
            }
            
            // Apply brakes
            if (wheel.hasBrakes && brakeInput > 0)
            {
                wheel.collider.brakeTorque = brakeInput * brakePower;
            }
            else
            {
                wheel.collider.brakeTorque = 0;
            }
        }
    }
    
    void ApplySteering()
    {
        float targetSteerAngle = moveInput.x * maxSteerAngle;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        
        if(useAckermannSteering)
        {
            // Apply Ackermann steering geometry
            float turnRadius = Mathf.Abs(wheelbase / Mathf.Sin(Mathf.Deg2Rad * currentSteerAngle));
            float ackermannAngleLeft = 0f;
            float ackermannAngleRight = 0f;
            
            if (Mathf.Abs(currentSteerAngle) > 0.1f)
            {
                if (currentSteerAngle > 0) // turning right
                {
                    ackermannAngleLeft = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (turnRadius + (trackWidth / 2)));
                    ackermannAngleRight = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (turnRadius - (trackWidth / 2)));
                }
                else // turning left
                {
                    ackermannAngleLeft = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (turnRadius - (trackWidth / 2)));
                    ackermannAngleRight = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (turnRadius + (trackWidth / 2)));
                }
                ackermannAngleLeft *= Mathf.Sign(currentSteerAngle);
                ackermannAngleRight *= Mathf.Sign(currentSteerAngle);
            }
            
            foreach (var wheel in wheels)
            {
                if (wheel.steerable)
                {
                    if (wheel.wheelType == WheelType.FrontLeft)
                        wheel.collider.steerAngle = ackermannAngleLeft;
                    else if (wheel.wheelType == WheelType.FrontRight)
                        wheel.collider.steerAngle = ackermannAngleRight;
                }
            }
        }
        else
        {
            // Simple steering
            foreach (var wheel in wheels)
            {
                if (wheel.steerable)
                {
                    wheel.collider.steerAngle = currentSteerAngle;
                }
            }
        }
    }
    
    void UpdateWheelVisuals()
    {
        foreach (var wheel in wheels)
        {
            if (wheel.wheelMesh != null)
            {
                Vector3 position;
                Quaternion rotation;
                wheel.collider.GetWorldPose(out position, out rotation);
                wheel.wheelMesh.position = position;
                wheel.wheelMesh.rotation = rotation;
            }
        }
    }
    
    void ApplyDownforce()
    {
        // Simple downforce based on velocity
        float downforce = carRigidbody.velocity.sqrMagnitude * downforceCoefficient;
        carRigidbody.AddForce(-transform.up * downforce);
    }
    
    void ApplyAntiRoll()
    {
        // Apply anti-roll forces to reduce body roll
        ApplyAntiRollToAxle(WheelType.FrontLeft, WheelType.FrontRight);
        ApplyAntiRollToAxle(WheelType.RearLeft, WheelType.RearRight);
    }
    
    void ApplyAntiRollToAxle(WheelType leftType, WheelType rightType)
    {
        Wheel leftWheel = GetWheelByType(leftType);
        Wheel rightWheel = GetWheelByType(rightType);
        
        if (leftWheel == null || rightWheel == null)
            return;
            
        WheelHit leftWheelHit, rightWheelHit;
        bool groundedLeft = leftWheel.collider.GetGroundHit(out leftWheelHit);
        bool groundedRight = rightWheel.collider.GetGroundHit(out rightWheelHit);
        
        float leftTravel = groundedLeft ? leftWheel.collider.transform.InverseTransformPoint(leftWheelHit.point).y - leftWheel.collider.radius : 1.0f;
        float rightTravel = groundedRight ? rightWheel.collider.transform.InverseTransformPoint(rightWheelHit.point).y - rightWheel.collider.radius : 1.0f;
        
        float antiRollForceAmount = (leftTravel - rightTravel) * antiRollForce;
        
        if (groundedLeft)
            carRigidbody.AddForceAtPosition(leftWheel.collider.transform.up * -antiRollForceAmount, leftWheel.collider.transform.position);
        if (groundedRight)
            carRigidbody.AddForceAtPosition(rightWheel.collider.transform.up * antiRollForceAmount, rightWheel.collider.transform.position);
    }
    
    Wheel GetWheelByType(WheelType type)
    {
        foreach (var wheel in wheels)
        {
            if (wheel.wheelType == type)
                return wheel;
        }
        return null;
    }
    
    // Network-related methods
    public void SetIsLocal(bool isLocal)
    {
        IsLocal = isLocal;
    }
    
    public void ApplyRemoteState(GameManager.PlayerStateData stateData, bool teleport = false)
    {
        // Will be implemented when we add networking
    }
    
    public void ApplyRemoteInput(GameManager.PlayerInputData inputData)
    {
        // Will be implemented when we add networking
    }
    
    public void Respawn(Vector3 position, Quaternion rotation)
    {
        carRigidbody.velocity = Vector3.zero;
        carRigidbody.angularVelocity = Vector3.zero;
        transform.position = position;
        transform.rotation = rotation;
    }

    // Updated OnDrawGizmos with more diagnostic info
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
            return;
            
        if (carRigidbody != null)
        {
            // Draw center of mass
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(carRigidbody.worldCenterOfMass, 0.1f);
            
            // Draw velocity vector
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(
                carRigidbody.worldCenterOfMass, 
                carRigidbody.worldCenterOfMass + carRigidbody.velocity * 0.2f
            );
        }
        
        // Draw wheel forces
        if (wheels != null)
        {
            foreach (var wheel in wheels)
            {
                if (wheel.collider != null && Application.isPlaying)
                {
                    // Get wheel position
                    Vector3 position;
                    Quaternion rotation;
                    wheel.collider.GetWorldPose(out position, out rotation);
                    
                    // Draw motor torque
                    Gizmos.color = Color.green;
                    float torqueVisualization = wheel.collider.motorTorque / 1000f; // Scale for visualization
                    Vector3 motorDirection = wheel.collider.transform.right * Mathf.Sign(wheel.collider.rpm);
                    Gizmos.DrawRay(position, motorDirection * torqueVisualization);
                    
                    // Draw wheel load (weight on the wheel)
                    WheelHit hit;
                    if (wheel.collider.GetGroundHit(out hit))
                    {
                        Gizmos.color = Color.yellow;
                        float normalForce = hit.force / 5000f; // Scale for visualization
                        Gizmos.DrawRay(position, Vector3.up * normalForce);
                        
                        // Draw friction
                        Gizmos.color = Color.red;
                        Gizmos.DrawRay(hit.point, hit.forwardDir * hit.forwardSlip * 0.5f);
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawRay(hit.point, hit.sidewaysDir * hit.sidewaysSlip * 0.5f);
                    }
                }
            }
        }
    }
}
