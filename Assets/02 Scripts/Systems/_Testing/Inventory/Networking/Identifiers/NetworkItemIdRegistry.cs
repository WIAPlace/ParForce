using System;
using System.Collections.Generic;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Maps the inventory model's 128-bit item-instance GUIDs to compact uint IDs.
    /// Keep one registry for one network identity scope (for example, one player's
    /// inventory + equipment). The mapping is session/runtime data, not save data.
    ///
    /// A sender assigns an ID once and includes it in network data. Receivers register
    /// that exact mapping or create a local GUID for an incoming network ID. Never
    /// assume independently created registries will allocate the same IDs on their own.
    /// </summary>
    public sealed class NetworkItemIdRegistry
    {
        private readonly Dictionary<SerializableGuid, uint> networkIdsByLocalId =
            new Dictionary<SerializableGuid, uint>();
        private readonly Dictionary<uint, SerializableGuid> localIdsByNetworkId =
            new Dictionary<uint, SerializableGuid>();

        private uint nextNetworkId = 1;

        public int Count => networkIdsByLocalId.Count;

        public bool TryGetOrAssign(SerializableGuid localItemId, out uint networkItemId)
        {
            networkItemId = 0;
            if (localItemId == SerializableGuid.Empty)
                return false;

            if (networkIdsByLocalId.TryGetValue(localItemId, out networkItemId))
                return true;

            if (!TryAllocateNetworkId(out networkItemId))
                return false;

            networkIdsByLocalId.Add(localItemId, networkItemId);
            localIdsByNetworkId.Add(networkItemId, localItemId);
            return true;
        }

        /// <summary>
        /// Registers an ID received from the authoritative sender. Re-registering the
        /// exact same pair is harmless; either side being mapped to a different value
        /// is rejected.
        /// </summary>
        public bool TryRegister(SerializableGuid localItemId, uint networkItemId)
        {
            if (localItemId == SerializableGuid.Empty || networkItemId == 0)
                return false;

            if (networkIdsByLocalId.TryGetValue(localItemId, out uint existingNetworkId))
                return existingNetworkId == networkItemId;

            if (localIdsByNetworkId.TryGetValue(networkItemId, out SerializableGuid existingLocalId))
                return existingLocalId == localItemId;

            networkIdsByLocalId.Add(localItemId, networkItemId);
            localIdsByNetworkId.Add(networkItemId, localItemId);
            AdvanceAllocatorPast(networkItemId);
            return true;
        }

        public bool TryGetNetworkId(SerializableGuid localItemId, out uint networkItemId)
        {
            return networkIdsByLocalId.TryGetValue(localItemId, out networkItemId);
        }

        public bool TryGetLocalId(uint networkItemId, out SerializableGuid localItemId)
        {
            return localIdsByNetworkId.TryGetValue(networkItemId, out localItemId);
        }

        /// <summary>
        /// Resolves an incoming network ID to a local model GUID. When the item is not
        /// known on this peer, a fresh local GUID is created and associated with the
        /// incoming ID; no GUID is transmitted over the network.
        /// </summary>
        public bool TryGetOrCreateLocalId(uint networkItemId, out SerializableGuid localItemId)
        {
            localItemId = SerializableGuid.Empty;
            if (networkItemId == 0)
                return false;

            if (localIdsByNetworkId.TryGetValue(networkItemId, out localItemId))
                return true;

            do
            {
                localItemId = SerializableGuid.NewGuid();
            }
            while (localItemId == SerializableGuid.Empty || networkIdsByLocalId.ContainsKey(localItemId));

            if (!TryRegister(localItemId, networkItemId))
            {
                localItemId = SerializableGuid.Empty;
                return false;
            }

            return true;
        }

        public bool RemoveByLocalId(SerializableGuid localItemId)
        {
            if (!networkIdsByLocalId.TryGetValue(localItemId, out uint networkItemId))
                return false;

            networkIdsByLocalId.Remove(localItemId);
            localIdsByNetworkId.Remove(networkItemId);
            return true;
        }

        public bool RemoveByNetworkId(uint networkItemId)
        {
            if (!localIdsByNetworkId.TryGetValue(networkItemId, out SerializableGuid localItemId))
                return false;

            localIdsByNetworkId.Remove(networkItemId);
            networkIdsByLocalId.Remove(localItemId);
            return true;
        }

        public void Clear()
        {
            networkIdsByLocalId.Clear();
            localIdsByNetworkId.Clear();
            nextNetworkId = 1;
        }

        private bool TryAllocateNetworkId(out uint networkItemId)
        {
            networkItemId = 0;
            uint candidate = nextNetworkId == 0 ? 1 : nextNetworkId;
            uint firstCandidate = candidate;

            do
            {
                if (!localIdsByNetworkId.ContainsKey(candidate))
                {
                    networkItemId = candidate;
                    nextNetworkId = candidate == uint.MaxValue ? 1 : candidate + 1;
                    return true;
                }

                candidate = candidate == uint.MaxValue ? 1 : candidate + 1;
            }
            while (candidate != firstCandidate);

            return false;
        }

        private void AdvanceAllocatorPast(uint usedNetworkId)
        {
            if (usedNetworkId == nextNetworkId)
                nextNetworkId = usedNetworkId == uint.MaxValue ? 1 : usedNetworkId + 1;
        }
    }
}
