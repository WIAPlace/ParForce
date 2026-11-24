using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Bridges equipment storage and an attribute model. It listens to equipment events and reconciles
    /// source-item modifiers without making either model depend on the other.
    /// </summary>
    public sealed class EquipmentEffectService : IDisposable
    {
        private readonly IEquipmentInventoryModel equipment;
        private readonly AttributeModel attributes;
        private readonly HashSet<SerializableGuid> activeSources = new HashSet<SerializableGuid>();
        private bool disposed;

        public EquipmentEffectService(IEquipmentInventoryModel equipment, AttributeModel attributes)
        {
            this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            this.attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));

            this.equipment.EquipmentChanged += HandleEquipmentChanged;
            Reconcile();
        }

        private void HandleEquipmentChanged(EquipmentChangedEvent change)
        {
            if (disposed || change == null || change.InventoryId != equipment.Id) return;
            Reconcile();
        }

        /// <summary>Rebuilds the active source set from the current equipment contents.</summary>
        public void Reconcile()
        {
            if (disposed) return;
            var currentlyEquipped = new Dictionary<SerializableGuid, Item>();
            foreach (Item item in equipment.Items)
            {
                if (item == null || item.details == null || item.Id == SerializableGuid.Empty) continue;
                currentlyEquipped[item.Id] = item;
            }

            foreach (SerializableGuid removedSource in activeSources.Where(id => !currentlyEquipped.ContainsKey(id)).ToList())
            {
                attributes.RemoveModifiersFromSource(removedSource);
                activeSources.Remove(removedSource);
            }

            foreach (KeyValuePair<SerializableGuid, Item> entry in currentlyEquipped)
            {
                Item item = entry.Value;
                IEnumerable<EquipmentEffectDefinition> definitions = item.details.EquipmentEffects ?? Enumerable.Empty<EquipmentEffectDefinition>();
                List<AttributeModifier> modifiers = definitions
                    .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.AttributeId))
                    .Select(definition => new AttributeModifier(entry.Key, definition.AttributeId,
                        definition.Value, definition.Operation))
                    .ToList();
                attributes.SetModifiersFromSource(entry.Key, modifiers);
                if (modifiers.Count > 0) activeSources.Add(entry.Key);
                else activeSources.Remove(entry.Key);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            equipment.EquipmentChanged -= HandleEquipmentChanged;
            foreach (SerializableGuid source in activeSources) attributes.RemoveModifiersFromSource(source);
            activeSources.Clear();
            disposed = true;
        }
    }
}
