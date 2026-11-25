using UnityEngine;
using System;
using System.Linq;
using UnityEngine.InputSystem;
using Systems.Inventory.Architecture;
using UnityEditor.Search;
using Systems.Inventory;
using Unity.VisualScripting;

public class InventoryTestOpenNewModel : MonoBehaviour
{
    [SerializeField] InputReader input;
    [SerializeField] InventoryGridLayout inventoryGrid;
    private bool inventoryOpen;
    GridInventoryModel chestModel;
    [SerializeField]InventoryPanelHost hostPanel;
    [SerializeField]private Item rewardItem;
    [SerializeField]InventoryPanelSession session;
    [SerializeField] InventoryPanelCanvas canvas;
    [SerializeField, Range(.01f, 3f)] float size=1;
    private float currentSize;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        input.InteractEvent += InteractTest;
        input.JumpEvent += JumpTest;
        session.ItemDropped += HandleItemDropped;

        chestModel = new GridInventoryModel(
        SerializableGuid.NewGuid(),
        "Wooden Chest",
        inventoryGrid
        );

        inventoryOpen = false;

        hostPanel.Close();
    }

    // Update is called once per frame
    void Update()
    {
        if(currentSize!=size){
            canvas.SetGlobalScale(size);
            currentSize = size;
        }
    }

    private void InteractTest()
    {
        inventoryOpen = !inventoryOpen;
        if(!inventoryOpen) hostPanel.Open(chestModel,null);
        else hostPanel.Close();
    }
    private void JumpTest()
    {
        /*
        hostPanel.TryAddItem(
            rewardItem,
            out InventoryOperationFailure failure
        );
        *///Debug.Log(failure);
    }
    private void HandleItemDropped(Item item, InventoryPanelView view)
    {
        
    }
}
