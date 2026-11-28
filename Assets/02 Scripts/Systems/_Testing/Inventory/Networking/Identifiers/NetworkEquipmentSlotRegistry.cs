using System;
using System.Collections.Generic;
using Systems.Inventory.Architecture;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Translates the inventory model's equipment slot strings into stable compact
    /// ushort IDs authored on each EquipmentSlotDefinition. Unlike list indices, these
    /// IDs remain unchanged when the visual/layout order is rearranged.
    /// </summary>
    public sealed class NetworkEquipmentSlotRegistry
    {
        private readonly Dictionary<string, ushort> networkIdsBySlotId;
        private readonly Dictionary<ushort, string> slotIdsByNetworkId;

        private NetworkEquipmentSlotRegistry(
            Dictionary<string, ushort> networkIdsBySlotId,
            Dictionary<ushort, string> slotIdsByNetworkId)
        {
            this.networkIdsBySlotId = networkIdsBySlotId;
            this.slotIdsByNetworkId = slotIdsByNetworkId;
        }

        public int Count => networkIdsBySlotId.Count;

        public static bool TryCreate(
            IReadOnlyList<EquipmentSlotDefinition> slots,
            out NetworkEquipmentSlotRegistry registry,
            out List<string> errors)
        {
            registry = null;
            errors = new List<string>();

            if (slots == null)
            {
                errors.Add("The equipment slot list is null.");
                return false;
            }

            var bySlotId = new Dictionary<string, ushort>(StringComparer.Ordinal);
            var byNetworkId = new Dictionary<ushort, string>();
            for (int i = 0; i < slots.Count; i++)
            {
                EquipmentSlotDefinition slot = slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId))
                {
                    errors.Add($"Equipment slot at index {i} is null or has an empty SlotId.");
                    continue;
                }

                if (slot.NetworkSlotId == 0)
                {
                    errors.Add($"Equipment slot '{slot.SlotId}' has no Network Slot ID. Zero is reserved.");
                    continue;
                }

                if (bySlotId.ContainsKey(slot.SlotId))
                {
                    errors.Add($"Equipment slot string ID '{slot.SlotId}' appears more than once.");
                    continue;
                }

                if (byNetworkId.TryGetValue(slot.NetworkSlotId, out string existingSlotId))
                {
                    errors.Add($"Network Slot ID {slot.NetworkSlotId} is assigned to both '{existingSlotId}' and '{slot.SlotId}'.");
                    continue;
                }

                bySlotId.Add(slot.SlotId, slot.NetworkSlotId);
                byNetworkId.Add(slot.NetworkSlotId, slot.SlotId);
            }

            if (errors.Count > 0)
                return false;

            registry = new NetworkEquipmentSlotRegistry(bySlotId, byNetworkId);
            return true;
        }

        public bool TryGetNetworkSlotId(string slotId, out ushort networkSlotId)
        {
            networkSlotId = 0;
            return !string.IsNullOrWhiteSpace(slotId) && networkIdsBySlotId.TryGetValue(slotId, out networkSlotId);
        }

        public bool TryGetSlotId(ushort networkSlotId, out string slotId)
        {
            return slotIdsByNetworkId.TryGetValue(networkSlotId, out slotId);
        }
    }
}
