using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>One uniquely identified equipment position and the types it accepts.</summary>
    [Serializable]
    public sealed class EquipmentSlotDefinition
    {
        [SerializeField] private string slotId;
        [SerializeField] private string displayName;
        [SerializeField] private List<EquipmentType> acceptedTypes = new List<EquipmentType>();
        [SerializeField, Tooltip("Stable compact ID used in multiplayer equipment messages. Zero means unassigned; use the Equipment Slot Layout inspector to assign IDs.")]
        private ushort networkSlotId;

        public string SlotId => slotId;
        public string DisplayName => displayName;
        public IReadOnlyList<EquipmentType> AcceptedTypes => acceptedTypes;
        public ushort NetworkSlotId => networkSlotId;

        public EquipmentSlotDefinition() { }

        public EquipmentSlotDefinition(string slotId, string displayName, IEnumerable<EquipmentType> acceptedTypes, ushort networkSlotId = 0)
        {
            this.slotId = slotId;
            this.displayName = displayName;
            this.acceptedTypes = acceptedTypes == null ? new List<EquipmentType>() : acceptedTypes.Distinct().ToList();
            this.networkSlotId = networkSlotId;
        }

        public bool Accepts(EquipmentType type) => acceptedTypes != null && acceptedTypes.Contains(type);
    }
}
