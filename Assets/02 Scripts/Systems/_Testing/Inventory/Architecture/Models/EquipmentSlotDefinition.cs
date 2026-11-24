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

        public string SlotId => slotId;
        public string DisplayName => displayName;
        public IReadOnlyList<EquipmentType> AcceptedTypes => acceptedTypes;

        public EquipmentSlotDefinition() { }

        public EquipmentSlotDefinition(string slotId, string displayName, IEnumerable<EquipmentType> acceptedTypes)
        {
            this.slotId = slotId;
            this.displayName = displayName;
            this.acceptedTypes = acceptedTypes == null ? new List<EquipmentType>() : acceptedTypes.Distinct().ToList();
        }

        public bool Accepts(EquipmentType type) => acceptedTypes != null && acceptedTypes.Contains(type);
    }
}
