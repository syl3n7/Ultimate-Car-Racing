using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 10f;
    public Vector3 offset = new Vector3(0, 5, -10);
    public bool lookAtTarget = true;
    
    void LateUpdate()
    {
        if (target == null)
            return;
            
        // Calculate desired position
        Vector3 desiredPosition = target.position + offset;
        
        // Smoothly move to that position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;
        
        // Optionally look at the target
        if (lookAtTarget)
        {
            transform.LookAt(target);
        }
    }
    
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}