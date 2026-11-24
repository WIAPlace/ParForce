using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Fixed-slot collection inventory. Empty slots remain addressable, allowing
    /// exact placement and local rearrangement. Each item occupies one indexed slot;
    /// grid footprints and rotation are provided by GridInventoryModel instead.
    /// </summary>
    public sealed class CollectionInventoryModel : ISlotInventoryModel
    {
        private readonly List<Item> slots;
        private readonly ReadOnlyCollection<Item> readOnlySlots;

        public SerializableGuid Id { get; }
        public string DisplayName { get; }
        public int Capacity => slots.Count;
        public int ItemCount => slots.Count(item => item != null);
        public IReadOnlyList<Item> Items => readOnlySlots;
        public event Action<InventoryChangedEvent> Changed;

        public CollectionInventoryModel(SerializableGuid id, string displayName, int capacity,
            IEnumerable<Item> startingItems = null)
        {
            if (id == SerializableGuid.Empty) throw new ArgumentException("An inventory must have a non-empty ID.", nameof(id));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Inventory" : displayName;
            slots = Enumerable.Repeat<Item>(null, capacity).ToList();
            readOnlySlots = slots.AsReadOnly();
            if (startingItems == null) return;
            foreach (Item item in startingItems)
            {
                if (item == null || item.Id == SerializableGuid.Empty || Contains(item.Id)) continue;
                int empty = slots.FindIndex(existing => existing == null);
                if (empty < 0) break;
                slots[empty] = item;
            }
        }

        public bool TryGetItem(SerializableGuid id, out Item item)
        {
            item = slots.FirstOrDefault(candidate => candidate != null && candidate.Id == id);
            return item != null;
        }

        public bool TryGetItemAt(int index, out Item item)
        {
            item = null;
            if (!IsValidIndex(index)) return false;
            item = slots[index];
            return item != null;
        }

        public bool TryGetSlotOf(SerializableGuid id, out int slotIndex)
        {
            slotIndex = slots.FindIndex(item => item != null && item.Id == id);
            return slotIndex >= 0;
        }

        public bool CanAccept(Item item, out InventoryOperationFailure failure)
        {
            int empty = slots.FindIndex(existing => existing == null);
            return CanAcceptAt(item, empty, out failure);
        }

        public bool CanAcceptAt(Item item, int index, out InventoryOperationFailure failure)
        {
            if (item == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (item.Id == SerializableGuid.Empty) { failure = InventoryOperationFailure.InvalidItemId; return false; }
            if (Contains(item.Id)) { failure = InventoryOperationFailure.DuplicateItem; return false; }
            int maxStack = InventoryItemMatching.GetMaxStack(item);
            if (item.quantity < 1 || item.quantity > maxStack) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!IsValidIndex(index)) { failure = InventoryOperationFailure.InventoryFull; return false; }
            if (slots[index] != null) { failure = InventoryOperationFailure.InventoryFull; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryAdd(Item item, out InventoryOperationFailure failure)
        {
            int index = slots.FindIndex(existing => existing == null);
            return TryAddAt(item, index, out failure);
        }

        public bool TrySetQuantity(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
        {
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            int maximum = InventoryItemMatching.GetMaxStack(item);
            if (quantity < 1 || quantity > maximum) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            item.quantity = quantity;
            RaiseChanged(InventoryChangeKind.StackChanged, itemInstanceId);
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryAddAt(Item item, int index, out InventoryOperationFailure failure)
        {
            if (!CanAcceptAt(item, index, out failure)) return false;
            slots[index] = item;
            RaiseChanged(InventoryChangeKind.ItemAdded, item.Id);
            return true;
        }

        public bool TryRemoveQuantity(SerializableGuid itemInstanceId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            removedQuantity = Math.Min(quantity, item.quantity);
            int remaining = item.quantity - removedQuantity;
            if (remaining == 0)
            {
                if (!TryRemove(itemInstanceId, out _, out failure)) { removedQuantity = 0; return false; }
            }
            else if (!TrySetQuantity(itemInstanceId, remaining, out failure)) { removedQuantity = 0; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryRemoveQuantity(Item item, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            if (item == null) { removedQuantity = 0; failure = InventoryOperationFailure.InvalidItem; return false; }
            return TryRemoveQuantity(item.Id, quantity, out removedQuantity, out failure);
        }

        public bool TryRemoveQuantity(ItemDetails definition, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            Item[] matches = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).ToArray();
            if (matches.Length == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            int remaining = quantity;
            foreach (Item item in matches)
            {
                if (remaining <= 0) break;
                if (!TryRemoveQuantity(item.Id, remaining, out int taken, out failure)) continue;
                removedQuantity += taken;
                remaining -= taken;
            }
            failure = InventoryOperationFailure.None;
            return removedQuantity > 0;
        }

        public bool TryRemoveQuantityAtIndex(int itemIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (itemIndex < 0 || itemIndex >= Items.Count || Items[itemIndex] == null)
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(Items[itemIndex].Id, quantity, out removedQuantity, out failure);
        }

        public bool TryRemoveQuantityAt(int slotIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure) =>
            TryRemoveQuantityAtIndex(slotIndex, quantity, out removedQuantity, out failure);

        public bool TryRemoveExact(ItemDetails definition, int quantity, out InventoryOperationFailure failure)
        {
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available < quantity) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryRemoveQuantity(definition, quantity, out int removed, out failure) || removed != quantity)
            { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryRemoveAll(ItemDetails definition, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(definition, available, out removedQuantity, out failure);
        }

        public bool TryRemove(SerializableGuid id, out Item removed, out InventoryOperationFailure failure)
        {
            int index = slots.FindIndex(candidate => candidate != null && candidate.Id == id);
            if (index < 0) { removed = null; failure = InventoryOperationFailure.ItemNotFound; return false; }
            removed = slots[index];
            slots[index] = null;
            failure = InventoryOperationFailure.None;
            RaiseChanged(InventoryChangeKind.ItemRemoved, id);
            return true;
        }

        public bool TryMove(SerializableGuid id, int destinationSlot, out InventoryOperationFailure failure)
        {
            if (!TryGetSlotOf(id, out int sourceSlot)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            if (!IsValidIndex(destinationSlot)) { failure = InventoryOperationFailure.InventoryFull; return false; }
            if (sourceSlot == destinationSlot) { failure = InventoryOperationFailure.None; return true; }
            if (slots[destinationSlot] != null) { failure = InventoryOperationFailure.InventoryFull; return false; }
            slots[destinationSlot] = slots[sourceSlot];
            slots[sourceSlot] = null;
            RaiseChanged(InventoryChangeKind.ItemRemoved, id);
            RaiseChanged(InventoryChangeKind.ItemAdded, id);
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool Contains(SerializableGuid id) => slots.Any(item => item != null && item.Id == id);
        private bool IsValidIndex(int index) => index >= 0 && index < slots.Count;
        private void RaiseChanged(InventoryChangeKind kind, SerializableGuid id) =>
            Changed?.Invoke(new InventoryChangedEvent(Id, kind, id));
    }
}
