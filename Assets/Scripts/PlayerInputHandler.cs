using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerInputHandler : MonoBehaviour
{
    private CarController carController;
    
    void Awake()
    {
        carController = GetComponent<CarController>();
    }
    
    // This will be called by the Input System
    public void OnMove(InputValue value)
    {
        if (carController != null && carController.IsLocal)
        {
            carController.moveInput = value.Get<Vector2>();
            carController.HasInputChanges = true;
        }
    }
}