using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>Identifies the mutation performed by an inventory model.</summary>
    public enum InventoryOperationKind
    {
        Add,
        AddAt,
        Remove,
        SetQuantity,
        RemoveQuantityUpTo,
        RemoveQuantityExact,
        RemoveAll,
        Move,
        MoveTo
    }

    /// <summary>A quantity snapshot for one item affected by an inventory operation.</summary>
    public readonly struct InventoryItemChange
    {
        public SerializableGuid ItemInstanceId { get; }
        public SerializableGuid ItemDefinitionId { get; }
        public int AffectedQuantity { get; }
        public int PreviousQuantity { get; }
        public int ResultingQuantity { get; }

        internal InventoryItemChange(SerializableGuid itemInstanceId, SerializableGuid itemDefinitionId,
            int affectedQuantity, int previousQuantity, int resultingQuantity)
        {
            ItemInstanceId = itemInstanceId;
            ItemDefinitionId = itemDefinitionId;
            AffectedQuantity = affectedQuantity;
            PreviousQuantity = previousQuantity;
            ResultingQuantity = resultingQuantity;
        }
    }

    /// <summary>
    /// Immutable outcome of a model mutation. Multi-stack operations expose every
    /// affected stack through ItemChanges; Remove also returns the detached runtime item.
    /// </summary>
    public sealed class InventoryOperationResult
    {
        public bool Success { get; }
        public InventoryOperationKind Operation { get; }
        public InventoryOperationFailure Failure { get; }
        public SerializableGuid InventoryId { get; }
        public int RequestedQuantity { get; }
        public int AffectedQuantity { get; }
        public IReadOnlyList<InventoryItemChange> ItemChanges { get; }
        public Item AddedItem { get; }
        public Item RemovedItem { get; }
        public string Message { get; }

        /// <summary>Convenience access to the first changed item, or Empty when nothing changed.</summary>
        public SerializableGuid ItemInstanceId => ItemChanges.Count > 0
            ? ItemChanges[0].ItemInstanceId : SerializableGuid.Empty;
        public SerializableGuid ItemDefinitionId => ItemChanges.Count > 0
            ? ItemChanges[0].ItemDefinitionId : SerializableGuid.Empty;
        public int PreviousQuantity => ItemChanges.Count > 0 ? ItemChanges[0].PreviousQuantity : 0;
        public int ResultingQuantity => ItemChanges.Count > 0 ? ItemChanges[0].ResultingQuantity : 0;

        private InventoryOperationResult(bool success, InventoryOperationKind operation,
            InventoryOperationFailure failure, SerializableGuid inventoryId, int requestedQuantity,
            IEnumerable<InventoryItemChange> itemChanges, Item addedItem, Item removedItem, string message)
        {
            Success = success;
            Operation = operation;
            Failure = success ? InventoryOperationFailure.None : failure;
            InventoryId = inventoryId;
            RequestedQuantity = requestedQuantity;
            List<InventoryItemChange> changes = itemChanges != null
                ? itemChanges.ToList() : new List<InventoryItemChange>();
            ItemChanges = changes.AsReadOnly();
            AffectedQuantity = changes.Sum(change => change.AffectedQuantity);
            AddedItem = addedItem;
            RemovedItem = removedItem;
            Message = message ?? string.Empty;
        }

        internal static InventoryOperationSnapshot Capture(IInventoryModel inventory) =>
            new InventoryOperationSnapshot(inventory);

        internal static InventoryOperationResult Complete(IInventoryModel inventory,
            InventoryOperationKind operation, int requestedQuantity, bool success,
            InventoryOperationFailure failure, InventoryOperationSnapshot before,
            Item removedItem = null, SerializableGuid focusItemId = default, Item addedItem = null)
        {
            List<InventoryItemChange> changes = new List<InventoryItemChange>();
            Dictionary<SerializableGuid, ItemState> after = inventory.Items
                .Where(item => item != null)
                .ToDictionary(item => item.Id, ItemState.From);
            List<SerializableGuid> ids = new List<SerializableGuid>(before.ItemOrder);
            foreach (Item item in inventory.Items)
                if (item != null && !before.Items.ContainsKey(item.Id))
                    ids.Add(item.Id);
            if (focusItemId != SerializableGuid.Empty &&
                (before.Items.ContainsKey(focusItemId) || after.ContainsKey(focusItemId)) &&
                !ids.Contains(focusItemId))
                ids.Add(focusItemId);

            foreach (SerializableGuid id in ids)
            {
                bool hadBefore = before.Items.TryGetValue(id, out ItemState oldState);
                bool hasAfter = after.TryGetValue(id, out ItemState newState);
                int previous = hadBefore ? oldState.Quantity : 0;
                int resulting = hasAfter ? newState.Quantity : 0;
                if (previous == resulting && (id != focusItemId || !success)) continue;
                SerializableGuid definitionId = hasAfter ? newState.DefinitionId : oldState.DefinitionId;
                int affected;
                switch (operation)
                {
                    case InventoryOperationKind.Add:
                    case InventoryOperationKind.AddAt:
                        affected = Math.Max(0, resulting - previous);
                        break;
                    case InventoryOperationKind.Remove:
                    case InventoryOperationKind.RemoveAll:
                    case InventoryOperationKind.RemoveQuantityUpTo:
                    case InventoryOperationKind.RemoveQuantityExact:
                        affected = Math.Max(0, previous - resulting);
                        break;
                    case InventoryOperationKind.Move:
                    case InventoryOperationKind.MoveTo:
                        affected = 0;
                        break;
                    default:
                        affected = Math.Abs(resulting - previous);
                        break;
                }
                changes.Add(new InventoryItemChange(id, definitionId, affected, previous, resulting));
            }

            return new InventoryOperationResult(success, operation, failure, inventory.Id,
                requestedQuantity, changes, addedItem, removedItem,
                success ? string.Empty : failure.ToString());
        }

        internal static InventoryOperationResult Failed(IInventoryModel inventory,
            InventoryOperationKind operation, int requestedQuantity, InventoryOperationFailure failure)
        {
            return Failed(inventory != null ? inventory.Id : SerializableGuid.Empty,
                operation, requestedQuantity, failure);
        }

        internal static InventoryOperationResult Failed(SerializableGuid inventoryId,
            InventoryOperationKind operation, int requestedQuantity, InventoryOperationFailure failure)
        {
            return new InventoryOperationResult(false, operation, failure, inventoryId,
                requestedQuantity, null, null, null, failure.ToString());
        }

        internal readonly struct ItemState
        {
            public readonly SerializableGuid DefinitionId;
            public readonly int Quantity;

            private ItemState(SerializableGuid definitionId, int quantity)
            {
                DefinitionId = definitionId;
                Quantity = quantity;
            }

            internal static ItemState From(Item item) => new ItemState(
                item.details != null ? item.details.Id : item.detailsId, item.quantity);
        }
    }

    internal sealed class InventoryOperationSnapshot
    {
        internal Dictionary<SerializableGuid, InventoryOperationResult.ItemState> Items { get; }
        internal List<SerializableGuid> ItemOrder { get; }

        internal InventoryOperationSnapshot(IInventoryModel inventory)
        {
            Items = new Dictionary<SerializableGuid, InventoryOperationResult.ItemState>();
            ItemOrder = new List<SerializableGuid>();
            foreach (Item item in inventory.Items)
            {
                if (item == null) continue;
                Items.Add(item.Id, InventoryOperationResult.ItemState.From(item));
                ItemOrder.Add(item.Id);
            }
        }
    }
}
