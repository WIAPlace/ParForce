using System;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Authoring data stored on an ItemDetails asset; contains no runtime item identity.</summary>
    [Serializable]
    public sealed class EquipmentEffectDefinition
    {
        [SerializeField] private string attributeId;
        [SerializeField] private float value;
        [SerializeField] private AttributeModifierOperation operation = AttributeModifierOperation.Flat;

        public string AttributeId => attributeId;
        public float Value => value;
        public AttributeModifierOperation Operation => operation;

        public EquipmentEffectDefinition() { }

        public EquipmentEffectDefinition(string attributeId, float value,
            AttributeModifierOperation operation = AttributeModifierOperation.Flat)
        {
            this.attributeId = attributeId;
            this.value = value;
            this.operation = operation;
        }
    }
}
