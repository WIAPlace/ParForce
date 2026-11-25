using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>Capability for inventories whose positions are named equipment slots.</summary>
    public interface IEquipmentInventoryModel : IInventoryModel
    {
        event System.Action<EquipmentChangedEvent> EquipmentChanged;
        IReadOnlyList<EquipmentSlotDefinition> SlotDefinitions { get; }
        int TotalEquipmentSlots { get; }
        int OccupiedEquipmentSlots { get; }
        int AvailableEquipmentSlots { get; }
        int TotalGridEquivalentOccupiedCells { get; }
        bool TryGetGridFootprint(SerializableGuid itemInstanceId, out int width, out int height, out int occupiedCells);
        bool TryGetItemAt(string slotId, out Item item);
        InventoryOperationResult RemoveQuantityAt(string slotId, int quantity);
        bool TryGetSlotOf(SerializableGuid itemInstanceId, out string slotId);
        bool CanAcceptAt(Item item, string slotId, out InventoryOperationFailure failure);
        InventoryOperationResult AddAt(Item item, string slotId);
        InventoryOperationResult Move(SerializableGuid itemInstanceId, string destinationSlotId);
    }
}
