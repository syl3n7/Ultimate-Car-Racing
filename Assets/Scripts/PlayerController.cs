using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltimateCarRacing.Networking;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Car Settings")]
    public float maxSpeed = 30f;
    public float acceleration = 10f;
    public float brakeForce = 15f;
    public float steeringSpeed = 100f;
    public float steeringAngle = 35f;
    public Transform centerOfMass;
    
    [Header("Wheels")]
    public Transform frontLeftWheel;
    public Transform frontRightWheel;
    public Transform rearLeftWheel;
    public Transform rearRightWheel;
    
    [Header("Visual")]
    public GameObject driverModel;
    public TextMesh playerNameText;
    
    [Header("Network Smoothing")]
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 10f;
    public float velocityLerpSpeed = 5f;
    public float inputSmoothing = 0.2f;
    public float desyncThreshold = 5f; // Teleport if more than this distance off
    
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
    
    // Smoothed input values for remote players
    private float targetThrottle;
    private float targetSteering;
    private float targetBrake;
    
    private float lastInputChangeTime;
    private bool isInitialized = false;

    // Add these properties to PlayerController
    public float LastStateTimestamp { get; set; }
    public float LastInputTimestamp { get; set; }
    
    void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
        
        // Set center of mass
        if (centerOfMass != null)
        {
            Rigidbody.centerOfMass = centerOfMass.localPosition;
        }
    }
    
    public void Initialize(string playerId, bool isLocal)
    {
        PlayerId = playerId;
        IsLocal = isLocal;
        isInitialized = true;
        
        // Setup visual appearance
        if (playerNameText != null)
        {
            playerNameText.text = playerId;
        }
        
        // Local player is kinematic until game starts
        if (!IsLocal)
        {
            // Initialize target transforms for remote players
            targetPosition = transform.position;
            targetRotation = transform.rotation;
            targetVelocity = Vector3.zero;
            targetAngularVelocity = Vector3.zero;
        }
    }
    
    void Update()
    {
        if (!isInitialized) return;
        
        if (IsLocal)
        {
            // Handle local player input
            HandleInput();
        }
        else
        {
            // Smooth remote player position and rotation
            SmoothRemoteTransform();
        }
        
        // Update wheel visuals
        UpdateWheels();
    }
    
    void FixedUpdate()
    {
        if (!isInitialized) return;
        
        if (IsLocal)
        {
            // Apply physics to local player
            ApplyDriving();
        }
    }
    
    private void HandleInput()
    {
        float prevThrottle = CurrentThrottle;
        float prevSteering = CurrentSteering;
        float prevBrake = CurrentBrake;
        
        // Get input
        CurrentThrottle = Input.GetAxis("Vertical");
        CurrentSteering = Input.GetAxis("Horizontal");
        CurrentBrake = Input.GetKey(KeyCode.Space) ? 1f : 0f;
        
        // Check if input has changed significantly
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
        // Apply forward/backward force
        float currentSpeed = Vector3.Dot(Rigidbody.linearVelocity, transform.forward);
        float speedRatio = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        
        // Reduce available acceleration at higher speeds
        float availableAcceleration = acceleration * (1f - speedRatio * 0.5f);
        
        // Apply acceleration force
        if (CurrentThrottle != 0)
        {
            Vector3 accelerationForce = transform.forward * CurrentThrottle * availableAcceleration;
            Rigidbody.AddForce(accelerationForce, ForceMode.Acceleration);
        }
        
        // Apply braking
        if (CurrentBrake > 0)
        {
            // Apply stronger braking force when moving faster
            Vector3 brakeForceVector = -Rigidbody.linearVelocity.normalized * brakeForce * CurrentBrake * speedRatio;
            Rigidbody.AddForce(brakeForceVector, ForceMode.Acceleration);
        }
        
        // Apply steering
        if (CurrentSteering != 0)
        {
            // Reduce steering effectiveness at higher speeds
            float steeringEffectiveness = Mathf.Lerp(1f, 0.5f, speedRatio);
            float turnAmount = CurrentSteering * steeringAngle * steeringEffectiveness;
            
            // Apply torque for steering
            Rigidbody.AddTorque(transform.up * turnAmount * steeringSpeed * Time.fixedDeltaTime, ForceMode.Acceleration);
        }
        
        // Apply artificial drag to limit top speed
        if (currentSpeed > maxSpeed)
        {
            Vector3 dragForce = -Rigidbody.linearVelocity.normalized * (currentSpeed - maxSpeed) * 0.5f;
            Rigidbody.AddForce(dragForce, ForceMode.Acceleration);
        }
    }
    
    // Add interpolation factor based on network conditions
    private void SmoothRemoteTransform()
    {
        // Calculate interpolation factor based on network conditions
        float lerpFactor = Time.deltaTime * Mathf.Clamp(positionLerpSpeed / NetworkManager.Instance.GetAverageLatency(), 0.5f, 2.0f);
        
        // Smooth position - but detect large desync
        if (Vector3.Distance(transform.position, targetPosition) > desyncThreshold)
        {
            // Teleport if desync is too large
            transform.position = targetPosition;
            Rigidbody.velocity = targetVelocity;
            transform.rotation = targetRotation;
            Rigidbody.angularVelocity = targetAngularVelocity;
        }
        else
        {
            // Otherwise smoothly lerp
            transform.position = Vector3.Lerp(transform.position, targetPosition, lerpFactor);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lerpFactor);
            Rigidbody.velocity = Vector3.Lerp(Rigidbody.velocity, targetVelocity, velocityLerpSpeed * Time.deltaTime);
            Rigidbody.angularVelocity = Vector3.Lerp(Rigidbody.angularVelocity, targetAngularVelocity, velocityLerpSpeed * Time.deltaTime);
        }
        
        // Smooth input values for prediction
        CurrentThrottle = Mathf.Lerp(CurrentThrottle, targetThrottle, inputSmoothing);
        CurrentSteering = Mathf.Lerp(CurrentSteering, targetSteering, inputSmoothing);
        CurrentBrake = Mathf.Lerp(CurrentBrake, targetBrake, inputSmoothing);
    }
    
    private void UpdateWheels()
    {
        // Update wheel visuals based on steering and speed
        if (frontLeftWheel != null && frontRightWheel != null)
        {
            // Front wheels turn with steering
            frontLeftWheel.localRotation = Quaternion.Euler(0, CurrentSteering * steeringAngle, 0);
            frontRightWheel.localRotation = Quaternion.Euler(0, CurrentSteering * steeringAngle, 0);
        }
        
        // Implement wheel rotation based on car speed in a real implementation
    }
    
    // Modify the ApplyRemoteState method to support teleporting
    public void ApplyRemoteState(GameManager.PlayerStateData stateData, bool teleport = false)
    {
        if (teleport)
        {
            // Teleport immediately to the new state
            transform.position = stateData.Position;
            transform.rotation = Quaternion.Euler(stateData.Rotation);
            Rigidbody.velocity = stateData.Velocity;
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
}