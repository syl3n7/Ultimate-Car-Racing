using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UltimateCarRacing.Networking;

[RequireComponent(typeof(Rigidbody))]
public class WheelColliderCarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeftWheelCollider;
    public WheelCollider frontRightWheelCollider;
    public WheelCollider rearLeftWheelCollider;
    public WheelCollider rearRightWheelCollider;

    [Header("Wheel Transforms")]
    public Transform frontLeftWheelTransform;
    public Transform frontRightWheelTransform;
    public Transform rearLeftWheelTransform;
    public Transform rearRightWheelTransform;

    [Header("Vehicle Settings")]
    public float motorForce = 1000f;
    public float brakeForce = 3000f;
    public float maxSteerAngle = 30f;
    public float wheelbase = 2.5f;
    public float trackwidth = 1.5f;
    
    [Header("Center of Mass")]
    public Vector3 centerOfMassOffset = new Vector3(0, -0.5f, 0);
    
    [Header("Visual")]
    public GameObject driverModel;
    public TextMesh playerNameText;
    
    [Header("Network Settings")]
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 10f;
    public float velocityLerpSpeed = 5f;
    public float inputSmoothing = 0.2f;
    public float desyncThreshold = 5f;
    
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
        
        // Set a lower center of mass for stability
        Rigidbody.centerOfMass = centerOfMassOffset;
    }
    
    public void Initialize(string playerId, bool isLocal)
    {
        if (isInitialized && PlayerId == playerId)
        {
            if (IsLocal != isLocal)
            {
                SetIsLocal(isLocal);
                SetupCamera();
                Debug.Log($"Updated player {playerId} local status to {isLocal}");
            }
            return;
        }
        
        Debug.Log($"Initializing player {playerId}, isLocal={isLocal}");
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
        
        UpdateWheelVisuals();
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
            Debug.LogWarning($"HandleInput called on non-local player {PlayerId}");
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
    
    private void ApplyDriving()
    {
        float motor = CurrentThrottle * motorForce;
        float steer = CurrentSteering * maxSteerAngle;
        float brake = CurrentBrake * brakeForce;
        
        // Apply motor torque to rear wheels
        rearLeftWheelCollider.motorTorque = motor;
        rearRightWheelCollider.motorTorque = motor;
        
        // Apply steering to front wheels (with Ackermann steering)
        if (CurrentSteering > 0)
        {
            frontLeftWheelCollider.steerAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (trackwidth / 2 + Mathf.Tan(Mathf.Deg2Rad * steer) * wheelbase));
            frontRightWheelCollider.steerAngle = steer;
        }
        else if (CurrentSteering < 0)
        {
            frontLeftWheelCollider.steerAngle = steer;
            frontRightWheelCollider.steerAngle = Mathf.Rad2Deg * Mathf.Atan(wheelbase / (-trackwidth / 2 + Mathf.Tan(Mathf.Deg2Rad * steer) * wheelbase));
        }
        else
        {
            frontLeftWheelCollider.steerAngle = frontRightWheelCollider.steerAngle = 0;
        }
        
        // Apply brakes to all wheels
        if (brake > 0)
        {
            frontLeftWheelCollider.brakeTorque = brake;
            frontRightWheelCollider.brakeTorque = brake;
            rearLeftWheelCollider.brakeTorque = brake;
            rearRightWheelCollider.brakeTorque = brake;
        }
        else
        {
            frontLeftWheelCollider.brakeTorque = 0;
            frontRightWheelCollider.brakeTorque = 0;
            rearLeftWheelCollider.brakeTorque = 0;
            rearRightWheelCollider.brakeTorque = 0;
        }
    }
    
    private void UpdateWheelVisuals()
    {
        UpdateWheelPose(frontLeftWheelCollider, frontLeftWheelTransform);
        UpdateWheelPose(frontRightWheelCollider, frontRightWheelTransform);
        UpdateWheelPose(rearLeftWheelCollider, rearLeftWheelTransform);
        UpdateWheelPose(rearRightWheelCollider, rearRightWheelTransform);
    }
    
    private void UpdateWheelPose(WheelCollider collider, Transform wheelTransform)
    {
        if (collider == null || wheelTransform == null) return;
        
        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);
        
        wheelTransform.position = position;
        wheelTransform.rotation = rotation;
    }
    
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
            Debug.Log($"Found camera on {PlayerId}, isLocal={IsLocal}");
            
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
            Debug.LogWarning($"No camera found on player {PlayerId}!");
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
    
    public void ApplyRemoteState(GameManager.PlayerStateData stateData, bool teleport = false)
    {
        if (teleport)
        {
            transform.position = stateData.Position;
            transform.rotation = Quaternion.Euler(stateData.Rotation);
            Rigidbody.linearVelocity = stateData.Velocity;
            Rigidbody.angularVelocity = stateData.AngularVelocity;
        }
        else
        {
            targetPosition = stateData.Position;
            targetRotation = Quaternion.Euler(stateData.Rotation);
            targetVelocity = stateData.Velocity;
            targetAngularVelocity = stateData.AngularVelocity;
        }
    }
    
    public void ApplyRemoteInput(GameManager.PlayerInputData inputData)
    {
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
}