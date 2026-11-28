using System.Runtime.InteropServices;
using Systems.Inventory.Architecture;
using Systems.Inventory.Architecture.Networking;
using UnityEngine;

public class InventoryToEquipmentHUB 
{
    private EquipmentManager equipment;
    private PlayerInventoryOwner inventory;
    private NetworkInventoryCoordinator coordinator; // may not need to be held here
    private IEquipmentInventoryModel equipModel;
    //private IGridInventoryModel gridModel;

    public InventoryToEquipmentHUB(EquipmentManager equ)
    {
        equipment = equ;
       
        NetworkInventoryCoordinator.LocalPlayerInventoryReady += Initialize;
        //Initialize();
    }

    public void Initialize(NetworkInventoryCoordinator coord)
    {
        coordinator = coord;
        inventory = coordinator.InventoryOwner;

        equipModel = inventory.Equipment;
        //gridModel = inventory.Inventory;
        equipModel.EquipmentChanged += HandleEquipmentChange;
    }    

    public void UnInitialize()
    {
        equipModel.EquipmentChanged -= HandleEquipmentChange;
    }

    private void HandleEquipmentChange(EquipmentChangedEvent ece)
    {
        // if not the equipment inventory leave
        if(ece.InventoryId!=equipModel.Id) return;
        
        ushort newId;
        if(ece.NewItem == null || ece.NewItem.Id == SerializableGuid.Empty){
            newId = 1; // empty hand 
        }
        else
        {
            newId = ItemLinkDatabase.Instance.GetItemByID(ece.NewItem.detailsId);
        }

        switch (ece.SlotId)
        {
            case "RightHand":
                equipment.rightHandItemID.Value = newId;
                break;
            case "LeftHand":
                equipment.leftHandItemID.Value = newId;
                break;
            default : break;
        }
        //Debug.Log(inventory.SlotWeight);
        //Debug.Log(ece.SlotId + " : " + newId);
    }

    
}
