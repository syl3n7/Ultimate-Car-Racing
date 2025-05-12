using UnityEngine;

[System.Serializable]
public class Wheel
{
    public WheelCollider collider;
    public Transform wheelMesh;
    public WheelType wheelType;
    public bool powered = false;
    public bool steerable = false;
    public bool hasBrakes = true;
}

[System.Serializable]
public enum WheelType
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight
}
