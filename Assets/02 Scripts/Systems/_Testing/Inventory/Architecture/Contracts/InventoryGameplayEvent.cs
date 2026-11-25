using System;

namespace Systems.Inventory.Architecture
{
    /// <summary>Meaningful inventory changes intended for gameplay systems, not UI refreshes.</summary>
    public enum InventoryGameplayEventKind
    {
        ItemAdded,
        ItemRemoved,
        ItemQuantityChanged,
        ItemMoved
    }

    /// <summary>Immutable gameplay notification generated after a successful inventory operation.</summary>
    public sealed class InventoryGameplayEvent
    {
        public SerializableGuid InventoryId { get; }
        public InventoryGameplayEventKind Kind { get; }
        public SerializableGuid ItemInstanceId { get; }
        public SerializableGuid ItemDefinitionId { get; }
        public int QuantityDelta { get; }
        public int PreviousQuantity { get; }
        public int ResultingQuantity { get; }
        public InventoryOperationKind Operation { get; }
        public InventoryOperationResult Result { get; }

        internal InventoryGameplayEvent(SerializableGuid inventoryId, InventoryGameplayEventKind kind,
            InventoryItemChange change, InventoryOperationResult result)
        {
            InventoryId = inventoryId;
            Kind = kind;
            ItemInstanceId = change.ItemInstanceId;
            ItemDefinitionId = change.ItemDefinitionId;
            QuantityDelta = change.ResultingQuantity - change.PreviousQuantity;
            PreviousQuantity = change.PreviousQuantity;
            ResultingQuantity = change.ResultingQuantity;
            Operation = result.Operation;
            Result = result;
        }
    }
}
