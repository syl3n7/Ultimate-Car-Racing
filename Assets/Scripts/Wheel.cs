using UnityEngine;

[System.Serializable]
public class Wheel
{
    public WheelCollider collider;
    public Transform wheelMesh;
    public WheelType wheelType;
}

[System.Serializable]
public enum WheelType
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight
}