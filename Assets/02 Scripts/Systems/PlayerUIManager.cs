using System;
using System.Runtime.InteropServices;
using Systems.Inventory.Architecture;
using Systems.Inventory.Architecture.Networking;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Open and close UI
/// </summary>
public class PlayerUIManager : MonoBehaviour
{
    [SerializeField] private InputReader input;
    [SerializeField] private LocalPlayerInventoryPanelBinder inventory;
    [SerializeField] private CinemachineInputAxisController axisController; // used for sensitivity

    private float xSense = 1;
    private float ySense = -1;

    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Initialize()
    {
        //Debug.Log("Initialized");
        input.OpenInventoryEvent += HandleOpenInventory;
        input.CloseUIEvent += HandleCloseUI;
    }
    public void UnInitialize()
    {
        input.OpenInventoryEvent -= HandleOpenInventory;
        input.CloseUIEvent -= HandleCloseUI;
    }

    
    private void HandleOpenInventory()
    {
        input.SetUI();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Open Both Inventory and Equipment.
        inventory.OpenPanels();

        // Save Current Sensitivity
        xSense = axisController.Controllers[0].Input.Gain;
        ySense = axisController.Controllers[1].Input.Gain;

        // Turn sensitivity to zero
        axisController.Controllers[0].Input.Gain = 0;
        axisController.Controllers[1].Input.Gain = 0;
    }

    private void HandleCloseUI()
    {
        //Debug.Log("Closed");
        InventoryPanelHost.CloseAll();
        input.SetPlayer();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Change this to Sensitivity Later on.
        axisController.Controllers[0].Input.Gain = xSense;
        axisController.Controllers[1].Input.Gain = ySense;
    }
}
