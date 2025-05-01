using UnityEngine;

// Data structures for network serialization
[System.Serializable]
public class PlayerStateData
{
    // Change to use SerializableVector3 instead of Unity's Vector3
    public SerializableVector3 Position;
    public SerializableVector3 Rotation;
    public SerializableVector3 Velocity;
    public SerializableVector3 AngularVelocity;
    public float Timestamp;
}

[System.Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;
    
    // Convert from Vector3
    public SerializableVector3(Vector3 vector)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }
    
    // Convert to Vector3
    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
    
    // Implicit conversion from Vector3
    public static implicit operator SerializableVector3(Vector3 vector)
    {
        return new SerializableVector3(vector);
    }
    
    // Implicit conversion to Vector3
    public static implicit operator Vector3(SerializableVector3 vector)
    {
        return new Vector3(vector.x, vector.y, vector.z);
    }
}