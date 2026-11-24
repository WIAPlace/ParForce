using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Engine-independent attribute storage. Base values and item-sourced modifiers are kept separately.
    /// Final value = (base value + flat modifiers) * (1 + additive percentage modifiers).
    /// </summary>
    public sealed class AttributeModel
    {
        private readonly Dictionary<string, float> baseValues = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<SerializableGuid, List<AttributeModifier>> modifiersBySource =
            new Dictionary<SerializableGuid, List<AttributeModifier>>();

        public event Action<string, float> AttributeChanged;

        public void SetBaseValue(string attributeId, float value)
        {
            ValidateAttributeId(attributeId);
            baseValues[attributeId] = value;
            RaiseChanged(attributeId);
        }

        public bool TryGetBaseValue(string attributeId, out float value)
        {
            return baseValues.TryGetValue(attributeId, out value);
        }

        public float GetValue(string attributeId)
        {
            ValidateAttributeId(attributeId);
            float baseValue = baseValues.TryGetValue(attributeId, out float foundBase) ? foundBase : 0f;
            float flat = 0f;
            float percent = 0f;

            foreach (AttributeModifier modifier in modifiersBySource.Values.SelectMany(list => list))
            {
                if (!string.Equals(modifier.AttributeId, attributeId, StringComparison.Ordinal)) continue;
                if (modifier.Operation == AttributeModifierOperation.Flat) flat += modifier.Value;
                else if (modifier.Operation == AttributeModifierOperation.AdditivePercent) percent += modifier.Value;
            }

            return (baseValue + flat) * (1f + percent);
        }

        /// <summary>Replaces all modifiers from this source, making repeated application idempotent.</summary>
        public void SetModifiersFromSource(SerializableGuid sourceItemId, IEnumerable<AttributeModifier> modifiers)
        {
            if (sourceItemId == SerializableGuid.Empty)
                throw new ArgumentException("A modifier source requires a valid item ID.", nameof(sourceItemId));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));

            var replacement = modifiers.ToList();
            if (replacement.Any(modifier => modifier == null || modifier.SourceItemId != sourceItemId))
                throw new ArgumentException("Every modifier must be non-null and match the supplied source item ID.", nameof(modifiers));

            HashSet<string> affected = new HashSet<string>(StringComparer.Ordinal);
            if (modifiersBySource.TryGetValue(sourceItemId, out List<AttributeModifier> previous))
                foreach (AttributeModifier modifier in previous) affected.Add(modifier.AttributeId);
            foreach (AttributeModifier modifier in replacement) affected.Add(modifier.AttributeId);

            if (replacement.Count == 0) modifiersBySource.Remove(sourceItemId);
            else modifiersBySource[sourceItemId] = replacement;

            foreach (string attributeId in affected) RaiseChanged(attributeId);
        }

        public bool RemoveModifiersFromSource(SerializableGuid sourceItemId)
        {
            if (!modifiersBySource.TryGetValue(sourceItemId, out List<AttributeModifier> existing)) return false;
            modifiersBySource.Remove(sourceItemId);
            foreach (string attributeId in existing.Select(modifier => modifier.AttributeId).Distinct(StringComparer.Ordinal))
                RaiseChanged(attributeId);
            return true;
        }

        public int GetModifierCount(SerializableGuid sourceItemId)
        {
            return modifiersBySource.TryGetValue(sourceItemId, out List<AttributeModifier> modifiers)
                ? modifiers.Count : 0;
        }

        public int ActiveSourceCount => modifiersBySource.Count;

        private void RaiseChanged(string attributeId) => AttributeChanged?.Invoke(attributeId, GetValue(attributeId));

        private static void ValidateAttributeId(string attributeId)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
                throw new ArgumentException("An attribute ID cannot be null or whitespace.", nameof(attributeId));
        }
    }
}
