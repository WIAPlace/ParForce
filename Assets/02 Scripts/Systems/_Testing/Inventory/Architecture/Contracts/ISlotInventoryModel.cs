namespace Systems.Inventory.Architecture
{
    /// <summary>Optional capability for inventories whose items occupy indexed slots.</summary>
    public interface ISlotInventoryModel : IInventoryModel
    {
        bool TryGetItemAt(int slotIndex, out Item item);
        bool TryGetSlotOf(SerializableGuid itemInstanceId, out int slotIndex);
        bool CanAcceptAt(Item item, int slotIndex, out InventoryOperationFailure failure);
        InventoryOperationResult AddAt(Item item, int slotIndex);
        InventoryOperationResult RemoveQuantityAt(int slotIndex, int quantity);
        InventoryOperationResult Move(SerializableGuid itemInstanceId, int destinationSlot);
    }
}
