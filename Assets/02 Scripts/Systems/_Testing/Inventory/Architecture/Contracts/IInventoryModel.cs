using System;
using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// The common contract for any inventory-like container. Every mutation returns
    /// a structured result; storage-specific placement remains on capability interfaces.
    /// </summary>
    public interface IInventoryModel
    {
        SerializableGuid Id { get; }
        string DisplayName { get; }
        /// <summary>Number of non-empty item entries. A stack counts as one entry.</summary>
        int ItemCount { get; }
        /// <summary>Storage-specific capacity, interpreted by the model capability.</summary>
        int Capacity { get; }
        IReadOnlyList<Item> Items { get; }

        event Action<InventoryChangedEvent> Changed;
        /// <summary>Gameplay-facing notifications emitted after successful mutations.</summary>
        event Action<InventoryGameplayEvent> GameplayEventRaised;

        bool TryGetItem(SerializableGuid itemInstanceId, out Item item);
        bool CanAccept(Item item, out InventoryOperationFailure failure);

        InventoryOperationResult Add(Item item);
        InventoryOperationResult SetQuantity(SerializableGuid itemInstanceId, int quantity);
        InventoryOperationResult Remove(SerializableGuid itemInstanceId);
        InventoryOperationResult RemoveQuantityUpTo(SerializableGuid itemInstanceId, int quantity);
        InventoryOperationResult RemoveQuantityUpTo(Item item, int quantity);
        InventoryOperationResult RemoveQuantityAtIndex(int itemIndex, int quantity);
        InventoryOperationResult RemoveQuantityExact(SerializableGuid itemInstanceId, int quantity);
        InventoryOperationResult RemoveQuantityUpTo(ItemDetails definition, int quantity);
        InventoryOperationResult RemoveQuantityExact(ItemDetails definition, int quantity);
        InventoryOperationResult RemoveAll(ItemDetails definition);
    }
}
