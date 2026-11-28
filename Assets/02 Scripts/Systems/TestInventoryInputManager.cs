using Systems.Inventory.Architecture;
using UnityEngine;

public class TestInventoryInputManager : MonoBehaviour
{
    [SerializeField] PlayerInventoryOwner owner;
    [SerializeField] private InputReader input;

    void Start()
    {
        input.InteractEvent += HandleInteract;
        input.InteractUIEvent += HandleInteractUI;

        owner.Initialize();
    }

    void HandleInteract() // pressed in Gameplay
    {
        owner.Open();
        
        input.SetUI();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void HandleInteractUI() // pressed in UI
    {
        owner.Close();

        input.SetPlayer();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
}
