using System;

namespace Systems.Inventory.Architecture
{
    public enum AttributeModifierOperation
    {
        Flat,
        AdditivePercent
    }

    /// <summary>A runtime modifier contributed by one specific item instance.</summary>
    [Serializable]
    public sealed class AttributeModifier
    {
        public SerializableGuid SourceItemId { get; }
        public string AttributeId { get; }
        public float Value { get; }
        public AttributeModifierOperation Operation { get; }

        public AttributeModifier(SerializableGuid sourceItemId, string attributeId, float value,
            AttributeModifierOperation operation)
        {
            if (sourceItemId == SerializableGuid.Empty)
                throw new ArgumentException("A modifier requires a valid source item ID.", nameof(sourceItemId));
            if (string.IsNullOrWhiteSpace(attributeId))
                throw new ArgumentException("A modifier requires an attribute ID.", nameof(attributeId));

            SourceItemId = sourceItemId;
            AttributeId = attributeId;
            Value = value;
            Operation = operation;
        }
    }
}
