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
        public event Action<InventoryGameplayEvent> GameplayEventRaised;

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

        private bool AddCore(Item item, out InventoryOperationFailure failure)
        {
            int index = slots.FindIndex(existing => existing == null);
            return AddAtCore(item, index, out failure);
        }

        private bool SetQuantityCore(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
        {
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            int maximum = InventoryItemMatching.GetMaxStack(item);
            if (quantity < 1 || quantity > maximum) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            item.quantity = quantity;
            RaiseChanged(InventoryChangeKind.StackChanged, itemInstanceId);
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool AddAtCore(Item item, int index, out InventoryOperationFailure failure)
        {
            if (!CanAcceptAt(item, index, out failure)) return false;
            slots[index] = item;
            RaiseChanged(InventoryChangeKind.ItemAdded, item.Id);
            return true;
        }

        private bool RemoveQuantityCore(SerializableGuid itemInstanceId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            removedQuantity = Math.Min(quantity, item.quantity);
            int remaining = item.quantity - removedQuantity;
            if (remaining == 0)
            {
                if (!RemoveCore(itemInstanceId, out _, out failure)) { removedQuantity = 0; return false; }
            }
            else if (!SetQuantityCore(itemInstanceId, remaining, out failure)) { removedQuantity = 0; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool RemoveQuantityCore(Item item, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            if (item == null) { removedQuantity = 0; failure = InventoryOperationFailure.InvalidItem; return false; }
            return RemoveQuantityCore(item.Id, quantity, out removedQuantity, out failure);
        }

        private bool RemoveQuantityCore(ItemDetails definition, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
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
                if (!RemoveQuantityCore(item.Id, remaining, out int taken, out failure)) continue;
                removedQuantity += taken;
                remaining -= taken;
            }
            failure = InventoryOperationFailure.None;
            return removedQuantity > 0;
        }

        private bool RemoveQuantityAtIndexCore(int itemIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (itemIndex < 0 || itemIndex >= Items.Count || Items[itemIndex] == null)
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(Items[itemIndex].Id, quantity, out removedQuantity, out failure);
        }

        private bool RemoveQuantityAtCore(int slotIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure) =>
            RemoveQuantityAtIndexCore(slotIndex, quantity, out removedQuantity, out failure);

        private bool RemoveQuantityExactCore(ItemDetails definition, int quantity, out InventoryOperationFailure failure)
        {
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            Item[] matches = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).ToArray();
            if (matches.Any(item => item.quantity < 1 || item.quantity > InventoryItemMatching.GetMaxStack(item)))
            { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            int available = matches.Sum(item => item.quantity);
            if (available < quantity) { failure = InventoryOperationFailure.InsufficientQuantity; return false; }
            if (!RemoveQuantityCore(definition, quantity, out int removed, out failure)) return false;
            if (removed != quantity) { failure = InventoryOperationFailure.InsufficientQuantity; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool RemoveAllCore(ItemDetails definition, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(definition, available, out removedQuantity, out failure);
        }

        private bool RemoveCore(SerializableGuid id, out Item removed, out InventoryOperationFailure failure)
        {
            int index = slots.FindIndex(candidate => candidate != null && candidate.Id == id);
            if (index < 0) { removed = null; failure = InventoryOperationFailure.ItemNotFound; return false; }
            removed = slots[index];
            slots[index] = null;
            failure = InventoryOperationFailure.None;
            RaiseChanged(InventoryChangeKind.ItemRemoved, id);
            return true;
        }

        private bool MoveCore(SerializableGuid id, int destinationSlot, out InventoryOperationFailure failure)
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


        public InventoryOperationResult Add(Item item)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = AddCore(item, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Add,
                item != null ? item.quantity : 0, success, failure, before,
                focusItemId: item != null ? item.Id : SerializableGuid.Empty, addedItem: success ? item : null));
        }

        public InventoryOperationResult SetQuantity(SerializableGuid itemInstanceId, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = SetQuantityCore(itemInstanceId, quantity, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.SetQuantity,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult Remove(SerializableGuid itemInstanceId)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveCore(itemInstanceId, out Item removedItem, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Remove,
                removedItem != null ? removedItem.quantity : 0, success, failure, before,
                removedItem, itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(SerializableGuid itemInstanceId, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(itemInstanceId, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(Item item, int quantity)
        {
            if (item == null)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityUpTo,
                    quantity, InventoryOperationFailure.InvalidItem);
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(item, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: item.Id));
        }

        public InventoryOperationResult RemoveQuantityExact(SerializableGuid itemInstanceId, int quantity)
        {
            if (quantity <= 0)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.InvalidQuantity);
            if (!TryGetItem(itemInstanceId, out Item item))
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.ItemNotFound);
            if (item.quantity < quantity)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.InsufficientQuantity);

            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(itemInstanceId, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityExact,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(ItemDetails definition, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(definition, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before));
        }

        public InventoryOperationResult RemoveQuantityExact(ItemDetails definition, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityExactCore(definition, quantity, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityExact,
                quantity, success, failure, before));
        }

        public InventoryOperationResult RemoveAll(ItemDetails definition)
        {
            int requested = definition == null ? 0 : Items
                .Where(item => InventoryItemMatching.MatchesDefinition(item, definition))
                .Sum(item => Math.Max(0, item.quantity));
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveAllCore(definition, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveAll,
                requested, success, failure, before));
        }

        public InventoryOperationResult AddAt(Item item, int slotIndex)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = AddAtCore(item, slotIndex, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.AddAt,
                item != null ? item.quantity : 0, success, failure, before,
                focusItemId: item != null ? item.Id : SerializableGuid.Empty, addedItem: success ? item : null));
        }

        public InventoryOperationResult RemoveQuantityAt(int slotIndex, int quantity) =>
            RemoveQuantityAtIndex(slotIndex, quantity);

        public InventoryOperationResult RemoveQuantityAtIndex(int itemIndex, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            SerializableGuid focusId = itemIndex >= 0 && itemIndex < Items.Count && Items[itemIndex] != null
                ? Items[itemIndex].Id : SerializableGuid.Empty;
            bool success = RemoveQuantityAtIndexCore(itemIndex, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: focusId));
        }

        public InventoryOperationResult Move(SerializableGuid itemInstanceId, int destinationSlot)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool changesPosition = TryGetSlotOf(itemInstanceId, out int sourceSlot) && sourceSlot != destinationSlot;
            bool success = MoveCore(itemInstanceId, destinationSlot, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Move,
                0, success, failure, before, focusItemId: changesPosition ? itemInstanceId : SerializableGuid.Empty));
        }

        private bool Contains(SerializableGuid id) => slots.Any(item => item != null && item.Id == id);
        private bool IsValidIndex(int index) => index >= 0 && index < slots.Count;
        private void RaiseChanged(InventoryChangeKind kind, SerializableGuid id) =>
            Changed?.Invoke(new InventoryChangedEvent(Id, kind, id));
        private InventoryOperationResult PublishGameplayEvents(InventoryOperationResult result)
        {
            InventoryGameplayEventPublisher.Publish(result, gameplayEvent => GameplayEventRaised?.Invoke(gameplayEvent));
            return result;
        }

    }
}
