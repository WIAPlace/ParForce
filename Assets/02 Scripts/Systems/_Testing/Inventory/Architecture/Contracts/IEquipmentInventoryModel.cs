using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>Capability for inventories whose positions are named equipment slots.</summary>
    public interface IEquipmentInventoryModel : IInventoryModel
    {
        event System.Action<EquipmentChangedEvent> EquipmentChanged;
        IReadOnlyList<EquipmentSlotDefinition> SlotDefinitions { get; }
        bool TryGetItemAt(string slotId, out Item item);
        bool TryGetSlotOf(SerializableGuid itemInstanceId, out string slotId);
        bool CanAcceptAt(Item item, string slotId, out InventoryOperationFailure failure);
        bool TryAddAt(Item item, string slotId, out InventoryOperationFailure failure);
        bool TryMove(SerializableGuid itemInstanceId, string destinationSlotId, out InventoryOperationFailure failure);
    }
}
