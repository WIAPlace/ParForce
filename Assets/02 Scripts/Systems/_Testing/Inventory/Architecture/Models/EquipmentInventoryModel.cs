using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Runtime equipment storage. Slot IDs are stable, unique keys; display names are presentation only.
    /// The model is independent of MonoBehaviours and UI Toolkit.
    /// </summary>
    public sealed class EquipmentInventoryModel : IEquipmentInventoryModel
    {
        private readonly List<EquipmentSlotDefinition> definitions;
        private readonly Dictionary<string, Item> slots;

        public SerializableGuid Id { get; }
        public string DisplayName { get; }
        public int Capacity => definitions.Count;
        public int TotalEquipmentSlots => definitions.Count;
        public int OccupiedEquipmentSlots => slots.Values.Count(item => item != null);
        public int AvailableEquipmentSlots => TotalEquipmentSlots - OccupiedEquipmentSlots;
        /// <summary>Sum of actual occupied item-shape cells across all equipped items.</summary>
        public int TotalGridEquivalentOccupiedCells => slots.Values
            .Where(item => item != null)
            .Sum(item => ItemGridShape.OccupiedCellCount(item.details));
        public int ItemCount => OccupiedEquipmentSlots;
        public IReadOnlyList<Item> Items => slots.Values.Where(item => item != null).ToList().AsReadOnly();
        public IReadOnlyList<EquipmentSlotDefinition> SlotDefinitions => definitions.AsReadOnly();
        public event Action<InventoryChangedEvent> Changed;
        public event Action<InventoryGameplayEvent> GameplayEventRaised;
        public event Action<EquipmentChangedEvent> EquipmentChanged;

        public EquipmentInventoryModel(SerializableGuid id, string displayName, IEnumerable<EquipmentSlotDefinition> slotDefinitions)
        {
            if (id == SerializableGuid.Empty) throw new ArgumentException("An inventory must have a non-empty ID.", nameof(id));
            if (slotDefinitions == null) throw new ArgumentNullException(nameof(slotDefinitions));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Equipment" : displayName;
            definitions = slotDefinitions.ToList();
            slots = new Dictionary<string, Item>(StringComparer.Ordinal);
            foreach (EquipmentSlotDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.SlotId))
                    throw new ArgumentException("Every equipment slot requires a non-empty stable slot ID.", nameof(slotDefinitions));
                if (definition.AcceptedTypes == null || definition.AcceptedTypes.Count == 0)
                    throw new ArgumentException($"Equipment slot '{definition.SlotId}' must accept at least one equipment type.", nameof(slotDefinitions));
                if (slots.ContainsKey(definition.SlotId))
                    throw new ArgumentException($"Duplicate equipment slot ID '{definition.SlotId}'. Slot IDs must be unique.", nameof(slotDefinitions));
                slots.Add(definition.SlotId, null);
            }
        }

        public bool TryGetItem(SerializableGuid itemInstanceId, out Item item)
        {
            item = slots.Values.FirstOrDefault(candidate => candidate != null && candidate.Id == itemInstanceId);
            return item != null;
        }

        public bool TryGetGridFootprint(SerializableGuid itemInstanceId, out int width, out int height, out int occupiedCells)
        {
            width = 0;
            height = 0;
            occupiedCells = 0;
            if (!TryGetItem(itemInstanceId, out Item item)) return false;

            ItemGridShape.GetRotatedSize(item.details, 0, out width, out height);
            occupiedCells = ItemGridShape.OccupiedCellCount(item.details);
            return true;
        }

        public bool TryGetItemAt(string slotId, out Item item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(slotId) || !slots.TryGetValue(slotId, out Item found)) return false;
            item = found;
            return item != null;
        }

        public bool TryGetSlotOf(SerializableGuid itemInstanceId, out string slotId)
        {
            foreach (KeyValuePair<string, Item> entry in slots)
                if (entry.Value != null && entry.Value.Id == itemInstanceId) { slotId = entry.Key; return true; }
            slotId = null;
            return false;
        }

        public bool CanAccept(Item item, out InventoryOperationFailure failure)
        {
            if (item == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (item.Id == SerializableGuid.Empty) { failure = InventoryOperationFailure.InvalidItemId; return false; }
            if (TryGetItem(item.Id, out _)) { failure = InventoryOperationFailure.DuplicateItem; return false; }
            if (item.quantity < 1 || item.quantity > InventoryItemMatching.GetMaxStack(item)) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            bool hasEmptySlot = false;
            foreach (EquipmentSlotDefinition definition in definitions)
            {
                if (slots[definition.SlotId] != null) continue;
                hasEmptySlot = true;
                if (CanAcceptAt(item, definition.SlotId, out failure)) return true;
            }
            failure = hasEmptySlot ? InventoryOperationFailure.IncompatibleItemType : InventoryOperationFailure.InventoryFull;
            return false;
        }

        public bool CanAcceptAt(Item item, string slotId, out InventoryOperationFailure failure)
        {
            if (item == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (item.Id == SerializableGuid.Empty) { failure = InventoryOperationFailure.InvalidItemId; return false; }
            if (TryGetItem(item.Id, out _)) { failure = InventoryOperationFailure.DuplicateItem; return false; }
            if (item.quantity < 1 || item.quantity > InventoryItemMatching.GetMaxStack(item)) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (string.IsNullOrWhiteSpace(slotId) || !slots.TryGetValue(slotId, out Item existing))
            { failure = InventoryOperationFailure.OutOfBounds; return false; }
            if (existing != null) { failure = InventoryOperationFailure.InventoryFull; return false; }
            EquipmentSlotDefinition definition = definitions.First(slot => slot.SlotId == slotId);
            if (item.details == null || item.details.category != ItemCategory.Equipment || !definition.Accepts(item.details.equipmentType))
            { failure = InventoryOperationFailure.IncompatibleItemType; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool AddCore(Item item, out InventoryOperationFailure failure)
        {
            if (!CanAccept(item, out failure)) return false;
            EquipmentSlotDefinition available = definitions.First(definition =>
                slots[definition.SlotId] == null && CanAcceptAt(item, definition.SlotId, out _));
            return AddAtCore(item, available.SlotId, out failure);
        }

        private bool SetQuantityCore(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
        {
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            if (quantity < 1 || quantity > InventoryItemMatching.GetMaxStack(item)) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            item.quantity = quantity;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.StackChanged, itemInstanceId));
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool AddAtCore(Item item, string slotId, out InventoryOperationFailure failure)
        {
            if (!CanAcceptAt(item, slotId, out failure)) return false;
            slots[slotId] = item;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.ItemAdded, item.Id));
            EquipmentChanged?.Invoke(new EquipmentChangedEvent(Id));
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

        private bool RemoveQuantityAtCore(string slotId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (!TryGetItemAt(slotId, out Item item))
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(item.Id, quantity, out removedQuantity, out failure);
        }

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

        private bool RemoveCore(SerializableGuid itemInstanceId, out Item removedItem, out InventoryOperationFailure failure)
        {
            if (!TryGetSlotOf(itemInstanceId, out string slotId))
            { removedItem = null; failure = InventoryOperationFailure.ItemNotFound; return false; }
            removedItem = slots[slotId];
            slots[slotId] = null;
            failure = InventoryOperationFailure.None;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.ItemRemoved, itemInstanceId));
            EquipmentChanged?.Invoke(new EquipmentChangedEvent(Id));
            return true;
        }

        private bool MoveCore(SerializableGuid itemInstanceId, string destinationSlotId, out InventoryOperationFailure failure)
        {
            if (!TryGetSlotOf(itemInstanceId, out string sourceSlotId))
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            if (sourceSlotId == destinationSlotId) { failure = InventoryOperationFailure.None; return true; }
            if (string.IsNullOrWhiteSpace(destinationSlotId) || !slots.TryGetValue(destinationSlotId, out Item existing))
            { failure = InventoryOperationFailure.OutOfBounds; return false; }
            if (existing != null) { failure = InventoryOperationFailure.InventoryFull; return false; }
            Item item = slots[sourceSlotId];
            EquipmentSlotDefinition definition = definitions.First(slot => slot.SlotId == destinationSlotId);
            if (item.details == null || item.details.category != ItemCategory.Equipment || !definition.Accepts(item.details.equipmentType))
            { failure = InventoryOperationFailure.IncompatibleItemType; return false; }
            slots[destinationSlotId] = item;
            slots[sourceSlotId] = null;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.ItemRemoved, itemInstanceId));
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.ItemAdded, itemInstanceId));
            EquipmentChanged?.Invoke(new EquipmentChangedEvent(Id));
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

        public InventoryOperationResult RemoveQuantityAtIndex(int itemIndex, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            SerializableGuid focusId = itemIndex >= 0 && itemIndex < Items.Count && Items[itemIndex] != null
                ? Items[itemIndex].Id : SerializableGuid.Empty;
            bool success = RemoveQuantityAtIndexCore(itemIndex, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: focusId));
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

        public InventoryOperationResult AddAt(Item item, string slotId)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = AddAtCore(item, slotId, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.AddAt,
                item != null ? item.quantity : 0, success, failure, before,
                focusItemId: item != null ? item.Id : SerializableGuid.Empty, addedItem: success ? item : null));
        }

        public InventoryOperationResult RemoveQuantityAt(string slotId, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            SerializableGuid focusId = TryGetItemAt(slotId, out Item item) ? item.Id : SerializableGuid.Empty;
            bool success = RemoveQuantityAtCore(slotId, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: focusId));
        }

        public InventoryOperationResult Move(SerializableGuid itemInstanceId, string destinationSlotId)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool changesPosition = TryGetSlotOf(itemInstanceId, out string sourceSlotId) &&
                !string.Equals(sourceSlotId, destinationSlotId, StringComparison.Ordinal);
            bool success = MoveCore(itemInstanceId, destinationSlotId, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Move,
                0, success, failure, before, focusItemId: changesPosition ? itemInstanceId : SerializableGuid.Empty));
        }

        private InventoryOperationResult PublishGameplayEvents(InventoryOperationResult result)
        {
            InventoryGameplayEventPublisher.Publish(result, gameplayEvent => GameplayEventRaised?.Invoke(gameplayEvent));
            return result;
        }

    }
}
