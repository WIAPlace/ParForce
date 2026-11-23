namespace Systems.Inventory.Architecture
{
    public enum InventoryChangeKind
    {
        ItemAdded,
        ItemRemoved,
        ContentsReplaced
    }

    public sealed class InventoryChangedEvent
    {
        public SerializableGuid InventoryId { get; }
        public InventoryChangeKind ChangeKind { get; }
        public SerializableGuid ItemInstanceId { get; }

        public InventoryChangedEvent(
            SerializableGuid inventoryId,
            InventoryChangeKind changeKind,
            SerializableGuid itemInstanceId)
        {
            InventoryId = inventoryId;
            ChangeKind = changeKind;
            ItemInstanceId = itemInstanceId;
        }
    }

    public enum InventoryOperationFailure
    {
        None,
        InvalidItem,
        InvalidItemId,
        DuplicateItem,
        InventoryFull,
        ItemNotFound
    }
}
