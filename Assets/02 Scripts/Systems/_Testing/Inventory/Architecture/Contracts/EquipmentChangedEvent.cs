namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Describes one equipment slot changing after the model has completed the mutation.
    /// Moves raise one event for the vacated slot and one for the newly occupied slot.
    /// </summary>
    public sealed class EquipmentChangedEvent
    {
        public SerializableGuid InventoryId { get; }
        public string SlotId { get; }
        public Item PreviousItem { get; }
        public Item NewItem { get; }

        public EquipmentChangedEvent(
            SerializableGuid inventoryId,
            string slotId,
            Item previousItem,
            Item newItem)
        {
            InventoryId = inventoryId;
            SlotId = slotId;
            PreviousItem = previousItem;
            NewItem = newItem;
        }
    }
}
