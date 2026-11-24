using System;
using UnityEngine;

namespace Systems.Inventory {
    /// <summary>
    /// Legacy save-file shape retained only so existing JSON saves can be migrated
    /// into the architecture inventory models. New saves use InventorySnapshot.
    /// </summary>
    [Serializable]
    public class LegacyInventoryData {
        [field: SerializeField] public SerializableGuid Id { get; set; }
        public Item[] Items;
        public int Capacity;
        public int Coins;
    }
}
