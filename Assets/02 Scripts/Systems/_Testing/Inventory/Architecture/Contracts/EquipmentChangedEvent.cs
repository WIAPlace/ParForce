namespace Systems.Inventory.Architecture
{
    /// <summary>Raised after the equipment model has completed a storage change.</summary>
    public sealed class EquipmentChangedEvent
    {
        public SerializableGuid InventoryId { get; }

        public EquipmentChangedEvent(SerializableGuid inventoryId)
        {
            InventoryId = inventoryId;
        }
    }
}
