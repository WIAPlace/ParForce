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
        public int ItemCount => slots.Values.Count(item => item != null);
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
            if (item.quantity != 1) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
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
            if (item.quantity != 1) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
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
            if (quantity != 1) { failure = InventoryOperationFailure.IncompatibleItemType; return false; }
            item.quantity = 1;
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
