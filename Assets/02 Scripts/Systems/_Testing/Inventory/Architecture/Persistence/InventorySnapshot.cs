using System;
using System.Collections.Generic;
using Systems.Inventory;

namespace Systems.Inventory.Architecture.Persistence
{
    /// <summary>Serializable save representation of one runtime inventory.</summary>
    [Serializable]
    public sealed class InventorySnapshot
    {
        public SerializableGuid InventoryId;
        public string StorageKind;
        public List<InventoryItemSnapshot> Items = new List<InventoryItemSnapshot>();
    }

    /// <summary>Serializable runtime item identity and its storage-specific location.</summary>
    [Serializable]
    public sealed class InventoryItemSnapshot
    {
        public SerializableGuid ItemId;
        public SerializableGuid ItemDefinitionId;
        public int Quantity;
        public int X;
        public int Y;
        public int RotationQuarterTurns;
        public int SlotIndex = -1;
        public string SlotId;
    }
}
