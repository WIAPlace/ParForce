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
        bool TryRemove(
            SerializableGuid itemInstanceId,
            out Item removedItem,
            out InventoryOperationFailure failure);
    }
}
