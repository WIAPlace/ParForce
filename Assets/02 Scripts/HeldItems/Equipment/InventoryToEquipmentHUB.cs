using Systems.Inventory.Architecture;
using UnityEngine;

public class InventoryToEquipmentHUB : MonoBehaviour
{
    [SerializeField] private EquipmentManager equipment;
    [SerializeField] private PlayerInventoryOwner inventory;
    private IEquipmentInventoryModel equipModel;
    //private IGridInventoryModel gridModel;

    public void Initialize()
    {
        equipModel = inventory.Equipment;
        //gridModel = inventory.Inventory;
        equipModel.EquipmentChanged += HandleEquipmentChange;
    }    

    public void UnInitialize()
    {
        equipModel.EquipmentChanged -= HandleEquipmentChange;
    }

    private void OnDestroy()
    {
        UnInitialize();
    }

    private void HandleEquipmentChange(EquipmentChangedEvent ece)
    {
        // if not the equipment inventory leave
        if(ece.InventoryId!=equipModel.Id) return;

        ushort newId;
        if(ece.NewItem == null || ece.NewItem.Id == SerializableGuid.Empty){
            newId = 0;
        }
        else
        {
            newId = ItemLinkDatabase.Instance.GetItemByID(ece.NewItem.Id);
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

    }
}
