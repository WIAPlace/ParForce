using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory {
    public static class ItemDatabase {
        static Dictionary<SerializableGuid, ItemDetails> itemDetailsDictionary;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize() {
            itemDetailsDictionary = null;
            EnsureInitialized();
        }

        static void EnsureInitialized() {
            if (itemDetailsDictionary != null) return;
            itemDetailsDictionary = new Dictionary<SerializableGuid, ItemDetails>();
            foreach (ItemDetails item in Resources.LoadAll<ItemDetails>("")) {
                if (item == null) continue;
                if (itemDetailsDictionary.ContainsKey(item.Id)) {
                    Debug.LogError($"Duplicate ItemDetails ID {item.Id} on '{item.name}'. Item IDs must be unique.");
                    continue;
                }
                itemDetailsDictionary.Add(item.Id, item);
            }
        }

        public static bool TryGetDetailsById(SerializableGuid id, out ItemDetails details) {
            EnsureInitialized();
            return itemDetailsDictionary.TryGetValue(id, out details);
        }

        public static ItemDetails GetDetailsById(SerializableGuid id) {
            if (TryGetDetailsById(id, out ItemDetails details)) return details;
            Debug.LogError($"Cannot find item details with id {id}");
            return null;
        }
    }
}