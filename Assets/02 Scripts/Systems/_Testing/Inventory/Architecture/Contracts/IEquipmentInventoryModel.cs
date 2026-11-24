using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>Capability for inventories whose positions are named equipment slots.</summary>
    public interface IEquipmentInventoryModel : IInventoryModel
    {
        event System.Action<EquipmentChangedEvent> EquipmentChanged;
        IReadOnlyList<EquipmentSlotDefinition> SlotDefinitions { get; }
        /// <summary>Total number of individually identified equipment slots.</summary>
        int TotalEquipmentSlots { get; }
        /// <summary>
        /// Number of equipment slots currently containing an item. A stack occupies
        /// one slot regardless of its quantity.
        /// </summary>
        int OccupiedEquipmentSlots { get; }
        /// <summary>Number of configured equipment slots currently empty.</summary>
        int AvailableEquipmentSlots { get; }
        /// <summary>Sum of occupied grid cells if every equipped item were placed in a grid.</summary>
        int TotalGridEquivalentOccupiedCells { get; }
        /// <summary>Gets the grid footprint described by an equipped item's ItemDetails.</summary>
        bool TryGetGridFootprint(SerializableGuid itemInstanceId, out int width, out int height, out int occupiedCells);
        bool TryGetItemAt(string slotId, out Item item);
        /// <summary>Removes quantity from the item occupying the named equipment slot.</summary>
        bool TryRemoveQuantityAt(string slotId, int quantity, out int removedQuantity, out InventoryOperationFailure failure);
        bool TryGetSlotOf(SerializableGuid itemInstanceId, out string slotId);
        bool CanAcceptAt(Item item, string slotId, out InventoryOperationFailure failure);
        bool TryAddAt(Item item, string slotId, out InventoryOperationFailure failure);
        bool TryMove(SerializableGuid itemInstanceId, string destinationSlotId, out InventoryOperationFailure failure);
    }
}
