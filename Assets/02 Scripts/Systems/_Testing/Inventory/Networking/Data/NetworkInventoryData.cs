using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture.Persistence;
using Unity.Netcode;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// A one-shot grid inventory snapshot for RPCs/custom messages. Inventory identity
    /// is carried by the owning NetworkObject/RPC context, not by a GUID in this payload.
    /// </summary>
    [Serializable]
    public struct NetworkInventoryData : INetworkSerializable
    {
        // A defensive allocation limit for malformed or unexpectedly large snapshots.
        // Raise this only if the game's inventory design legitimately requires it.
        public const int MaximumEntries = 4096;

        public uint Revision;
        public NetworkInventoryItemData[] Items;

        public int Count => Items == null ? 0 : Items.Length;

        public bool IsValid(out string error)
        {
            error = null;
            if (Items == null)
                return true;
            if (Items.Length > MaximumEntries)
            {
                error = $"Grid inventory contains {Items.Length} entries; maximum supported is {MaximumEntries}.";
                return false;
            }

            var instanceIds = new HashSet<uint>();
            for (int i = 0; i < Items.Length; i++)
            {
                if (!Items[i].IsValid)
                {
                    error = $"Grid inventory entry {i} is invalid.";
                    return false;
                }
                if (!instanceIds.Add(Items[i].InstanceId))
                {
                    error = $"Grid inventory contains duplicate item instance ID {Items[i].InstanceId}.";
                    return false;
                }
            }
            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Revision);
            int count = Items == null ? 0 : Items.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                ValidateSerializedCount(count);
                Items = new NetworkInventoryItemData[count];
            }
            else if (count < 0 || count > MaximumEntries)
            {
                throw new InvalidOperationException($"Cannot serialize {count} grid inventory entries; maximum is {MaximumEntries}.");
            }

            for (int i = 0; i < count; i++)
            {
                NetworkInventoryItemData item = serializer.IsReader ? default : Items[i];
                serializer.SerializeValue(ref item);
                if (serializer.IsReader)
                    Items[i] = item;
            }
        }

        public static bool TryCreateFromSnapshot(
            InventorySnapshot snapshot,
            NetworkItemDefinitionRegistry definitions,
            NetworkItemIdRegistry instanceIds,
            uint revision,
            out NetworkInventoryData networkData,
            out string error)
        {
            networkData = default;
            if (snapshot == null) { error = "Inventory snapshot is null."; return false; }
            if (snapshot.StorageKind != "Grid") { error = $"Expected a Grid snapshot, received '{snapshot.StorageKind}'."; return false; }
            if (definitions == null) { error = "Network item definition registry is null."; return false; }
            if (instanceIds == null) { error = "Network item instance ID registry is null."; return false; }
            if (snapshot.Items == null) { error = "Grid snapshot item list is null."; return false; }
            if (snapshot.Items.Count > MaximumEntries) { error = $"Grid snapshot has {snapshot.Items.Count} items; maximum is {MaximumEntries}."; return false; }

            var result = new NetworkInventoryItemData[snapshot.Items.Count];
            var seenLocalIds = new HashSet<SerializableGuid>();
            for (int i = 0; i < snapshot.Items.Count; i++)
            {
                InventoryItemSnapshot item = snapshot.Items[i];
                if (item == null || item.ItemId == SerializableGuid.Empty || item.Quantity < 1 ||
                    item.X < 0 || item.Y < 0 || item.RotationQuarterTurns < 0 || item.RotationQuarterTurns > 3)
                {
                    error = $"Grid snapshot item {i} has invalid identity, quantity, position, or rotation.";
                    return false;
                }
                if (!seenLocalIds.Add(item.ItemId)) { error = $"Grid snapshot contains duplicate local item ID {item.ItemId}."; return false; }
                if (!definitions.TryGetNetworkId(item.ItemDefinitionId, out ushort definitionId))
                {
                    error = $"Item definition GUID {item.ItemDefinitionId} is not present in the network definition catalog.";
                    return false;
                }

                if (!instanceIds.TryGetOrAssign(item.ItemId, out uint instanceId))
                {
                    error = $"Could not assign a compact network ID to item {item.ItemId}.";
                    return false;
                }

                result[i] = new NetworkInventoryItemData
                {
                    DefinitionId = definitionId,
                    InstanceId = instanceId,
                    Quantity = item.Quantity,
                    X = item.X,
                    Y = item.Y,
                    RotationQuarterTurns = (byte)item.RotationQuarterTurns
                };
            }

            networkData = new NetworkInventoryData { Revision = revision, Items = result };
            return networkData.IsValid(out error);
        }

        public bool TryCreateSnapshot(
            SerializableGuid localInventoryId,
            NetworkItemDefinitionRegistry definitions,
            NetworkItemIdRegistry instanceIds,
            out InventorySnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (localInventoryId == SerializableGuid.Empty) { error = "Local inventory ID is empty."; return false; }
            if (definitions == null) { error = "Network item definition registry is null."; return false; }
            if (instanceIds == null) { error = "Network item instance ID registry is null."; return false; }
            if (!IsValid(out error)) return false;

            var result = new InventorySnapshot
            {
                InventoryId = localInventoryId,
                StorageKind = "Grid",
                Items = new List<InventoryItemSnapshot>(Count)
            };

            foreach (NetworkInventoryItemData item in Items ?? Array.Empty<NetworkInventoryItemData>())
            {
                if (!definitions.TryGetDefinition(item.DefinitionId, out ItemDetails details))
                {
                    error = $"Unknown network item definition ID {item.DefinitionId}.";
                    return false;
                }
                if (!instanceIds.TryGetOrCreateLocalId(item.InstanceId, out SerializableGuid localItemId))
                {
                    error = $"Could not resolve network item instance ID {item.InstanceId}.";
                    return false;
                }

                result.Items.Add(new InventoryItemSnapshot
                {
                    ItemId = localItemId,
                    ItemDefinitionId = details.Id,
                    Quantity = item.Quantity,
                    X = item.X,
                    Y = item.Y,
                    RotationQuarterTurns = item.RotationQuarterTurns
                });
            }

            snapshot = result;
            error = null;
            return true;
        }

        private static void ValidateSerializedCount(int count)
        {
            if (count < 0 || count > MaximumEntries)
                throw new InvalidOperationException($"Received grid inventory entry count {count}; maximum is {MaximumEntries}.");
        }
    }
}
