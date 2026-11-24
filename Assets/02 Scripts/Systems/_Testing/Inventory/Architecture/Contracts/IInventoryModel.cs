using System;
using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// The common contract for any inventory-like container.
    /// Storage-specific capabilities (grid placement, equipment slots, etc.)
    /// should be exposed through separate interfaces rather than added here.
    /// </summary>
    public interface IInventoryModel
    {
        SerializableGuid Id { get; }
        string DisplayName { get; }
        int ItemCount { get; }
        int Capacity { get; }
        IReadOnlyList<Item> Items { get; }

        event Action<InventoryChangedEvent> Changed;

        bool TryGetItem(SerializableGuid itemInstanceId, out Item item);
        bool CanAccept(Item item, out InventoryOperationFailure failure);
        bool TryAdd(Item item, out InventoryOperationFailure failure);
        bool TrySetQuantity(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure);

        /// <summary>Removes up to the requested quantity from one item instance. Partial removal is allowed.</summary>
        bool TryRemoveQuantity(SerializableGuid itemInstanceId, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        /// <summary>Convenience overload that removes quantity from a specific runtime item instance.</summary>
        bool TryRemoveQuantity(Item item, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        /// <summary>Removes up to the requested quantity across stacks matching this item definition.</summary>
        bool TryRemoveQuantity(ItemDetails definition, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        /// <summary>Removes quantity from the item at its index in the model's Items collection (not a grid cell).</summary>
        bool TryRemoveQuantityAtIndex(int itemIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        /// <summary>Removes the requested quantity only if the entire amount is available; otherwise changes nothing.</summary>
        bool TryRemoveExact(ItemDetails definition, int quantity, out InventoryOperationFailure failure);
        /// <summary>Removes all stacks matching the item definition and reports the total quantity removed.</summary>
        bool TryRemoveAll(ItemDetails definition, out int removedQuantity, out InventoryOperationFailure failure);

        bool TryRemove(
            SerializableGuid itemInstanceId,
            out Item removedItem,
            out InventoryOperationFailure failure);
    }
}
