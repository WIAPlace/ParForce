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

        public bool TryAdd(Item item, out InventoryOperationFailure failure)
        {
            if (!CanAccept(item, out failure)) return false;
            EquipmentSlotDefinition available = definitions.First(definition =>
                slots[definition.SlotId] == null && CanAcceptAt(item, definition.SlotId, out _));
            return TryAddAt(item, available.SlotId, out failure);
        }

        public bool TrySetQuantity(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
        {
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            if (quantity < 1 || quantity > InventoryItemMatching.GetMaxStack(item)) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            item.quantity = quantity;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.StackChanged, itemInstanceId));
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryAddAt(Item item, string slotId, out InventoryOperationFailure failure)
        {
            if (!CanAcceptAt(item, slotId, out failure)) return false;
            slots[slotId] = item;
            Changed?.Invoke(new InventoryChangedEvent(Id, InventoryChangeKind.ItemAdded, item.Id));
            EquipmentChanged?.Invoke(new EquipmentChangedEvent(Id));
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

        public bool TryRemoveQuantityAt(string slotId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (!TryGetItemAt(slotId, out Item item))
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(item.Id, quantity, out removedQuantity, out failure);
        }

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

        public bool TryRemove(SerializableGuid itemInstanceId, out Item removedItem, out InventoryOperationFailure failure)
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

        public bool TryMove(SerializableGuid itemInstanceId, string destinationSlotId, out InventoryOperationFailure failure)
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
    }
}
