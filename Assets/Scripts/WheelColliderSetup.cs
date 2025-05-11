using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class WheelColliderSetup : MonoBehaviour
{
    [Header("Automatic Setup")]
    public Transform frontLeftWheelTransform;
    public Transform frontRightWheelTransform;
    public Transform rearLeftWheelTransform;
    public Transform rearRightWheelTransform;
    
    [Header("Wheel Collider Settings")]
    public float wheelMass = 20f;
    public float wheelRadius = 0.35f;
    public float suspensionDistance = 0.3f;
    public float suspensionSpring = 35000f;
    public float suspensionDamper = 4500f;
    public float forwardFriction = 1.5f;
    public float sidewaysFriction = 1.5f;
    
    [Header("Generated Wheel Colliders")]
    public WheelCollider frontLeftCollider;
    public WheelCollider frontRightCollider;
    public WheelCollider rearLeftCollider;
    public WheelCollider rearRightCollider;
    
    [Header("Car Settings")]
    public float wheelbase = 2.6f;
    public float trackWidth = 1.5f;
    
    public CarController carController;
    public Wheel[] wheels;
    
    private void Reset()
    {
        // Try to find wheel transforms automatically when component is first added
        Transform[] childTransforms = GetComponentsInChildren<Transform>();
        
        foreach (Transform child in childTransforms)
        {
            string name = child.name.ToLower();
            
            if (name.Contains("wheel") || name.Contains("tire"))
            {
                if (name.Contains("front") || name.Contains("f_"))
                {
                    if (name.Contains("left") || name.Contains("l_"))
                        frontLeftWheelTransform = child;
                    else if (name.Contains("right") || name.Contains("r_"))
                        frontRightWheelTransform = child;
                }
                else if (name.Contains("rear") || name.Contains("back") || name.Contains("r_"))
                {
                    if (name.Contains("left") || name.Contains("l_"))
                        rearLeftWheelTransform = child;
                    else if (name.Contains("right") || name.Contains("r_"))
                        rearRightWheelTransform = child;
                }
            }
        }
        
        // Try to find car controller if it exists
        carController = GetComponent<CarController>();
    }
    
    public void SetupWheelColliders()
    {
        // Create parent for wheel colliders if it doesn't exist
        Transform wheelCollidersParent = transform.Find("WheelColliders");
        if (wheelCollidersParent == null)
        {
            GameObject wheelCollidersObj = new GameObject("WheelColliders");
            wheelCollidersParent = wheelCollidersObj.transform;
            wheelCollidersParent.SetParent(transform);
            wheelCollidersParent.localPosition = Vector3.zero;
            wheelCollidersParent.localRotation = Quaternion.identity;
        }

        // Set up all four wheel colliders
        frontLeftCollider = SetupWheelCollider(frontLeftWheelTransform, "FrontLeftCollider", wheelCollidersParent);
        frontRightCollider = SetupWheelCollider(frontRightWheelTransform, "FrontRightCollider", wheelCollidersParent);
        rearLeftCollider = SetupWheelCollider(rearLeftWheelTransform, "RearLeftCollider", wheelCollidersParent);
        rearRightCollider = SetupWheelCollider(rearRightWheelTransform, "RearRightCollider", wheelCollidersParent);
        
        // Calculate wheelbase and track width from wheel positions
        if (frontLeftCollider != null && rearLeftCollider != null)
        {
            Vector3 frontPos = frontLeftCollider.transform.position;
            Vector3 rearPos = rearLeftCollider.transform.position;
            wheelbase = Vector3.Distance(new Vector3(0, 0, frontPos.z), new Vector3(0, 0, rearPos.z));
        }
        
        if (frontLeftCollider != null && frontRightCollider != null)
        {
            Vector3 leftPos = frontLeftCollider.transform.position;
            Vector3 rightPos = frontRightCollider.transform.position;
            trackWidth = Vector3.Distance(new Vector3(leftPos.x, 0, 0), new Vector3(rightPos.x, 0, 0));
        }
        
        // Create and set up wheel array for CarController
        if (carController != null)
        {
            List<Wheel> wheelsList = new List<Wheel>();
            
            if (frontLeftCollider != null && frontLeftWheelTransform != null)
            {
                Wheel wheel = new Wheel();
                wheel.collider = frontLeftCollider;
                wheel.wheelMesh = frontLeftWheelTransform;
                wheel.wheelType = WheelType.FrontLeft;
                wheelsList.Add(wheel);
            }
            
            if (frontRightCollider != null && frontRightWheelTransform != null)
            {
                Wheel wheel = new Wheel();
                wheel.collider = frontRightCollider;
                wheel.wheelMesh = frontRightWheelTransform;
                wheel.wheelType = WheelType.FrontRight;
                wheelsList.Add(wheel);
            }
            
            if (rearLeftCollider != null && rearLeftWheelTransform != null)
            {
                Wheel wheel = new Wheel();
                wheel.collider = rearLeftCollider;
                wheel.wheelMesh = rearLeftWheelTransform;
                wheel.wheelType = WheelType.RearLeft;
                wheelsList.Add(wheel);
            }
            
            if (rearRightCollider != null && rearRightWheelTransform != null)
            {
                Wheel wheel = new Wheel();
                wheel.collider = rearRightCollider;
                wheel.wheelMesh = rearRightWheelTransform;
                wheel.wheelType = WheelType.RearRight;
                wheelsList.Add(wheel);
            }
            
            wheels = wheelsList.ToArray();
            carController.wheels = wheels;
            carController.wheelbase = wheelbase;
            carController.trackwidth = trackWidth;
        }
    }
    
    private WheelCollider SetupWheelCollider(Transform wheelTransform, string colliderName, Transform parent)
    {
        if (wheelTransform == null)
        {
            Debug.LogWarning($"Wheel transform is missing for {colliderName}");
            return null;
        }
        
        // Check if collider already exists
        Transform existingCollider = parent.Find(colliderName);
        GameObject colliderObject;
        
        if (existingCollider != null)
        {
            colliderObject = existingCollider.gameObject;
        }
        else
        {
            // Create new collider object
            colliderObject = new GameObject(colliderName);
            colliderObject.transform.SetParent(parent);
        }
        
        // Position the collider at the wheel position
        Vector3 wheelPosition = wheelTransform.position;
        colliderObject.transform.position = wheelPosition;
        colliderObject.transform.rotation = transform.rotation; // Use car's rotation
        
        // Add or get wheel collider component
        WheelCollider wheelCollider = colliderObject.GetComponent<WheelCollider>();
        if (wheelCollider == null)
        {
            wheelCollider = colliderObject.AddComponent<WheelCollider>();
        }
        
        // Configure wheel collider
        JointSpring spring = wheelCollider.suspensionSpring;
        spring.spring = suspensionSpring;
        spring.damper = suspensionDamper;
        spring.targetPosition = 0.5f;
        
        wheelCollider.suspensionSpring = spring;
        wheelCollider.suspensionDistance = suspensionDistance;
        wheelCollider.mass = wheelMass;
        wheelCollider.radius = wheelRadius;
        wheelCollider.wheelDampingRate = 1.0f;
        
        // Set wheel friction
        WheelFrictionCurve fFriction = wheelCollider.forwardFriction;
        fFriction.stiffness = forwardFriction;
        wheelCollider.forwardFriction = fFriction;
        
        WheelFrictionCurve sFriction = wheelCollider.sidewaysFriction;
        sFriction.stiffness = sidewaysFriction;
        wheelCollider.sidewaysFriction = sFriction;
        
        return wheelCollider;
    }

#if UNITY_EDITOR
    [ContextMenu("Setup Wheel Colliders")]
    private void ContextMenuSetup()
    {
        SetupWheelColliders();
        
        // Mark the scene as dirty
        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(gameObject);
        if (wheels != null)
        {
            foreach (Wheel wheel in wheels)
            {
                if (wheel.collider != null)
                    EditorUtility.SetDirty(wheel.collider.gameObject);
            }
        }
    }
#endif
}