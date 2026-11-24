namespace Systems.Inventory.Architecture
{
    /// <summary>Optional capability for inventories whose items occupy indexed slots.</summary>
    public interface ISlotInventoryModel : IInventoryModel
    {
        bool TryGetItemAt(int slotIndex, out Item item);
        bool TryGetSlotOf(SerializableGuid itemInstanceId, out int slotIndex);
        bool CanAcceptAt(Item item, int slotIndex, out InventoryOperationFailure failure);
        bool TryAddAt(Item item, int slotIndex, out InventoryOperationFailure failure);
        /// <summary>Removes quantity from the item occupying this fixed slot index.</summary>
        bool TryRemoveQuantityAt(int slotIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        bool TryMove(SerializableGuid itemInstanceId, int destinationSlot, out InventoryOperationFailure failure);
    }
}
