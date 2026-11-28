using System;
using System.Collections.Generic;
using Systems.Inventory;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Runtime lookup between stable ItemDetails GUIDs and compact, catalog-assigned
    /// network definition IDs. ID zero is reserved and is never a valid definition.
    /// Build one registry from the same NetworkItemDefinitionCatalog on every peer.
    /// </summary>
    public sealed class NetworkItemDefinitionRegistry
    {
        private readonly Dictionary<ushort, ItemDetails> definitionsByNetworkId;
        private readonly Dictionary<SerializableGuid, ushort> networkIdsByDefinitionGuid;

        private NetworkItemDefinitionRegistry(
            Dictionary<ushort, ItemDetails> definitionsByNetworkId,
            Dictionary<SerializableGuid, ushort> networkIdsByDefinitionGuid)
        {
            this.definitionsByNetworkId = definitionsByNetworkId;
            this.networkIdsByDefinitionGuid = networkIdsByDefinitionGuid;
        }

        public int Count => definitionsByNetworkId.Count;

        public static bool TryCreate(
            IEnumerable<ItemDetails> definitions,
            out NetworkItemDefinitionRegistry registry,
            out List<string> errors)
        {
            registry = null;
            errors = new List<string>();

            if (definitions == null)
            {
                errors.Add("The item definition collection is null.");
                return false;
            }

            var byNetworkId = new Dictionary<ushort, ItemDetails>();
            var byGuid = new Dictionary<SerializableGuid, ushort>();
            var seenAssets = new HashSet<ItemDetails>();

            foreach (ItemDetails details in definitions)
            {
                if (details == null)
                {
                    errors.Add("The network item catalog contains a missing ItemDetails reference.");
                    continue;
                }

                if (!seenAssets.Add(details))
                {
                    errors.Add($"Item definition '{details.name}' appears more than once in the network catalog.");
                    continue;
                }

                if (details.Id == SerializableGuid.Empty)
                {
                    errors.Add($"Item definition '{details.name}' has an empty internal GUID.");
                    continue;
                }

                if (details.NetworkDefinitionId == 0)
                {
                    errors.Add($"Item definition '{details.name}' has no Network Definition ID. Zero is reserved.");
                    continue;
                }

                if (byNetworkId.TryGetValue(details.NetworkDefinitionId, out ItemDetails existingNetworkId))
                {
                    errors.Add($"Network Definition ID {details.NetworkDefinitionId} is assigned to both '{existingNetworkId.name}' and '{details.name}'.");
                    continue;
                }

                if (byGuid.TryGetValue(details.Id, out ushort existingDefinitionId))
                {
                    errors.Add($"Internal definition GUID {details.Id} is assigned to both network IDs {existingDefinitionId} and {details.NetworkDefinitionId}.");
                    continue;
                }

                byNetworkId.Add(details.NetworkDefinitionId, details);
                byGuid.Add(details.Id, details.NetworkDefinitionId);
            }

            if (errors.Count > 0)
                return false;

            registry = new NetworkItemDefinitionRegistry(byNetworkId, byGuid);
            return true;
        }

        public bool TryGetNetworkId(SerializableGuid definitionGuid, out ushort networkDefinitionId)
        {
            return networkIdsByDefinitionGuid.TryGetValue(definitionGuid, out networkDefinitionId);
        }

        public bool TryGetNetworkId(ItemDetails details, out ushort networkDefinitionId)
        {
            networkDefinitionId = 0;
            return details != null && networkIdsByDefinitionGuid.TryGetValue(details.Id, out networkDefinitionId);
        }

        public bool TryGetDefinition(ushort networkDefinitionId, out ItemDetails details)
        {
            details = null;
            return networkDefinitionId != 0 && definitionsByNetworkId.TryGetValue(networkDefinitionId, out details);
        }
    }
}
