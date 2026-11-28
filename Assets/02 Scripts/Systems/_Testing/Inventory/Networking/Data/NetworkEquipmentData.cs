using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture.Persistence;
using Unity.Netcode;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>One-shot equipment snapshot. Slot strings and all GUIDs stay local.</summary>
    [Serializable]
    public struct NetworkEquipmentData : INetworkSerializable
    {
        public const int MaximumEntries = 256;

        public uint Revision;
        public NetworkEquipmentItemData[] Items;

        public int Count => Items == null ? 0 : Items.Length;

        public bool IsValid(out string error)
        {
            error = null;
            if (Items == null)
                return true;
            if (Items.Length > MaximumEntries)
            {
                error = $"Equipment contains {Items.Length} entries; maximum supported is {MaximumEntries}.";
                return false;
            }

            var instanceIds = new HashSet<uint>();
            var slots = new HashSet<ushort>();
            for (int i = 0; i < Items.Length; i++)
            {
                if (!Items[i].IsValid) { error = $"Equipment entry {i} is invalid."; return false; }
                if (!instanceIds.Add(Items[i].InstanceId)) { error = $"Equipment contains duplicate item instance ID {Items[i].InstanceId}."; return false; }
                if (!slots.Add(Items[i].NetworkSlotId)) { error = $"Equipment contains duplicate network slot ID {Items[i].NetworkSlotId}."; return false; }
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
                Items = new NetworkEquipmentItemData[count];
            }
            else if (count < 0 || count > MaximumEntries)
            {
                throw new InvalidOperationException($"Cannot serialize {count} equipment entries; maximum is {MaximumEntries}.");
            }

            for (int i = 0; i < count; i++)
            {
                NetworkEquipmentItemData item = serializer.IsReader ? default : Items[i];
                serializer.SerializeValue(ref item);
                if (serializer.IsReader)
                    Items[i] = item;
            }
        }

        public static bool TryCreateFromSnapshot(
            InventorySnapshot snapshot,
            NetworkItemDefinitionRegistry definitions,
            NetworkItemIdRegistry instanceIds,
            NetworkEquipmentSlotRegistry slots,
            uint revision,
            out NetworkEquipmentData networkData,
            out string error)
        {
            networkData = default;
            if (snapshot == null) { error = "Equipment snapshot is null."; return false; }
            if (snapshot.StorageKind != "Equipment") { error = $"Expected an Equipment snapshot, received '{snapshot.StorageKind}'."; return false; }
            if (definitions == null) { error = "Network item definition registry is null."; return false; }
            if (instanceIds == null) { error = "Network item instance ID registry is null."; return false; }
            if (slots == null) { error = "Network equipment slot registry is null."; return false; }
            if (snapshot.Items == null) { error = "Equipment snapshot item list is null."; return false; }
            if (snapshot.Items.Count > MaximumEntries) { error = $"Equipment snapshot has {snapshot.Items.Count} items; maximum is {MaximumEntries}."; return false; }

            var result = new NetworkEquipmentItemData[snapshot.Items.Count];
            var seenLocalIds = new HashSet<SerializableGuid>();
            var seenSlots = new HashSet<ushort>();
            for (int i = 0; i < snapshot.Items.Count; i++)
            {
                InventoryItemSnapshot item = snapshot.Items[i];
                if (item == null || item.ItemId == SerializableGuid.Empty || item.Quantity < 1)
                {
                    error = $"Equipment snapshot item {i} has invalid identity or quantity.";
                    return false;
                }
                if (!seenLocalIds.Add(item.ItemId)) { error = $"Equipment snapshot contains duplicate local item ID {item.ItemId}."; return false; }
                if (!definitions.TryGetNetworkId(item.ItemDefinitionId, out ushort definitionId))
                {
                    error = $"Item definition GUID {item.ItemDefinitionId} is not present in the network definition catalog.";
                    return false;
                }
                if (!slots.TryGetNetworkSlotId(item.SlotId, out ushort networkSlotId))
                {
                    error = $"Equipment slot '{item.SlotId}' is not present in the active network slot registry.";
                    return false;
                }
                if (!seenSlots.Add(networkSlotId)) { error = $"Equipment snapshot contains multiple items in slot '{item.SlotId}'."; return false; }
                if (!instanceIds.TryGetOrAssign(item.ItemId, out uint instanceId))
                {
                    error = $"Could not assign a compact network ID to item {item.ItemId}.";
                    return false;
                }

                result[i] = new NetworkEquipmentItemData
                {
                    DefinitionId = definitionId,
                    InstanceId = instanceId,
                    Quantity = item.Quantity,
                    NetworkSlotId = networkSlotId
                };
            }

            networkData = new NetworkEquipmentData { Revision = revision, Items = result };
            return networkData.IsValid(out error);
        }

        public bool TryCreateSnapshot(
            SerializableGuid localInventoryId,
            NetworkItemDefinitionRegistry definitions,
            NetworkItemIdRegistry instanceIds,
            NetworkEquipmentSlotRegistry slots,
            out InventorySnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (localInventoryId == SerializableGuid.Empty) { error = "Local equipment inventory ID is empty."; return false; }
            if (definitions == null) { error = "Network item definition registry is null."; return false; }
            if (instanceIds == null) { error = "Network item instance ID registry is null."; return false; }
            if (slots == null) { error = "Network equipment slot registry is null."; return false; }
            if (!IsValid(out error)) return false;

            var result = new InventorySnapshot
            {
                InventoryId = localInventoryId,
                StorageKind = "Equipment",
                Items = new List<InventoryItemSnapshot>(Count)
            };

            foreach (NetworkEquipmentItemData item in Items ?? Array.Empty<NetworkEquipmentItemData>())
            {
                if (!definitions.TryGetDefinition(item.DefinitionId, out ItemDetails details))
                {
                    error = $"Unknown network item definition ID {item.DefinitionId}.";
                    return false;
                }
                if (!slots.TryGetSlotId(item.NetworkSlotId, out string slotId))
                {
                    error = $"Unknown network equipment slot ID {item.NetworkSlotId}.";
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
                    SlotId = slotId
                });
            }

            snapshot = result;
            error = null;
            return true;
        }

        private static void ValidateSerializedCount(int count)
        {
            if (count < 0 || count > MaximumEntries)
                throw new InvalidOperationException($"Received equipment entry count {count}; maximum is {MaximumEntries}.");
        }
    }
}
