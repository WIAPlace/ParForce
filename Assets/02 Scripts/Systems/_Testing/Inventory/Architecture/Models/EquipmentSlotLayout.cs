using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Reusable authoring asset defining a character/container's named equipment slots.</summary>
    [CreateAssetMenu(fileName = "New Equipment Slot Layout", menuName = "Inventory/Equipment Slot Layout")]
    public sealed class EquipmentSlotLayout : ScriptableObject
    {
        [SerializeField] private List<EquipmentSlotDefinition> slots = new List<EquipmentSlotDefinition>();
        public IReadOnlyList<EquipmentSlotDefinition> Slots => slots;
    }
}
