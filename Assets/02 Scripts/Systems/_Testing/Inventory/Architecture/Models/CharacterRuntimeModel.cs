using System;
using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Owns the runtime gameplay state associated with one character-like entity.
    /// This is a plain C# composition root: it has no MonoBehaviour, scene, or player-controller dependency.
    /// Each instance owns its own equipment, attributes, and equipment-effect subscription.
    /// </summary>
    public sealed class CharacterRuntimeModel : IDisposable
    {
        private readonly EquipmentEffectService equipmentEffects;
        private bool disposed;

        public SerializableGuid Id { get; }
        public string DisplayName { get; }
        public EquipmentInventoryModel Equipment { get; }
        public AttributeModel Attributes { get; }
        public bool IsDisposed => disposed;

        /// <summary>Forwarded attribute notifications for systems that only need to observe this owner.</summary>
        public event Action<string, float> AttributeChanged;

        public CharacterRuntimeModel(
            SerializableGuid id,
            string displayName,
            IEnumerable<EquipmentSlotDefinition> equipmentSlots,
            IEnumerable<KeyValuePair<string, float>> baseAttributes = null)
        {
            if (id == SerializableGuid.Empty)
                throw new ArgumentException("A runtime owner requires a non-empty ID.", nameof(id));
            if (equipmentSlots == null)
                throw new ArgumentNullException(nameof(equipmentSlots));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Character" : displayName;
            Equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), DisplayName + " Equipment", equipmentSlots);
            Attributes = new AttributeModel();

            if (baseAttributes != null)
            {
                foreach (KeyValuePair<string, float> attribute in baseAttributes)
                    Attributes.SetBaseValue(attribute.Key, attribute.Value);
            }

            Attributes.AttributeChanged += HandleAttributeChanged;
            equipmentEffects = new EquipmentEffectService(Equipment, Attributes);
        }

        public void SetBaseAttribute(string attributeId, float value)
        {
            ThrowIfDisposed();
            Attributes.SetBaseValue(attributeId, value);
        }

        public float GetAttribute(string attributeId)
        {
            ThrowIfDisposed();
            return Attributes.GetValue(attributeId);
        }

        private void HandleAttributeChanged(string attributeId, float value)
        {
            if (!disposed) AttributeChanged?.Invoke(attributeId, value);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CharacterRuntimeModel));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Attributes.AttributeChanged -= HandleAttributeChanged;
            equipmentEffects.Dispose();
            AttributeChanged = null;
        }
    }
}
