using System;
using System.Collections.Generic;
using System.Linq;
using Systems.Inventory;
using UnityEngine;

namespace Systems.Inventory.Architecture.Persistence
{
    /// <summary>
    /// Registers runtime inventory models with the save system. Models remain independent
    /// of MonoBehaviours and persistence; scene hosts register models as they become available.
    /// Registration is intentionally independent of panel lifetime, so a model remains
    /// registered after its host closes or is destroyed. Call Unregister when the actual
    /// inventory/container is permanently removed from the game.
    /// </summary>
    public static class InventoryPersistence
    {
        private static readonly Dictionary<SerializableGuid, IInventoryModel> Registered =
            new Dictionary<SerializableGuid, IInventoryModel>();
        private static readonly Dictionary<SerializableGuid, InventorySnapshot> Pending =
            new Dictionary<SerializableGuid, InventorySnapshot>();
        private static LegacyInventoryData pendingLegacyData;
        public static bool LegacyMigrationPending => pendingLegacyData != null;

        /// <summary>
        /// Registers a model for saving and returns the canonical registered instance.
        /// If the ID is already registered, the existing model is returned and reused.
        /// </summary>
        public static IInventoryModel Register(IInventoryModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.Id == SerializableGuid.Empty)
                throw new ArgumentException("Registered inventories require a stable, non-empty ID.", nameof(model));

            if (Registered.TryGetValue(model.Id, out IInventoryModel existing) && !ReferenceEquals(existing, model))
            {
                Debug.LogWarning($"A different inventory model with ID {model.Id} is already registered. Reusing the existing model; call Unregister before intentionally replacing an inventory.");
                model = existing;
            }
            else
            {
                Registered[model.Id] = model;
            }

            if (Pending.TryGetValue(model.Id, out InventorySnapshot snapshot))
            {
                Restore(model, snapshot);
                Pending.Remove(model.Id);
            }
            else if (pendingLegacyData != null && pendingLegacyData.Id == model.Id)
            {
                RestoreLegacy(model, pendingLegacyData);
                pendingLegacyData = null;
            }
            return model;
        }

        /// <summary>Stops persisting an inventory that has been permanently removed.</summary>
        public static void Unregister(IInventoryModel model)
        {
            if (model == null) return;
            if (Registered.TryGetValue(model.Id, out IInventoryModel registered) && ReferenceEquals(registered, model))
                Registered.Remove(model.Id);
            Pending.Remove(model.Id);
        }

        public static List<InventorySnapshot> CaptureAll()
        {
            // Preserve snapshots for inventories in unloaded scenes that have not yet
            // been recreated/registered during this play session.
            var snapshots = new Dictionary<SerializableGuid, InventorySnapshot>(Pending);
            foreach (IInventoryModel model in Registered.Values)
                snapshots[model.Id] = Capture(model);
            return snapshots.Values.OrderBy(snapshot => snapshot.InventoryId.ToString()).ToList();
        }

        public static void BeginRestore(IEnumerable<InventorySnapshot> snapshots, LegacyInventoryData legacyData = null)
        {
            Pending.Clear();
            if (snapshots != null)
            {
                foreach (InventorySnapshot snapshot in snapshots)
                {
                    if (snapshot == null || snapshot.InventoryId == SerializableGuid.Empty) continue;
                    Pending[snapshot.InventoryId] = snapshot;
                }
            }
            pendingLegacyData = legacyData;

            // Models that are already alive (for example DontDestroyOnLoad containers)
            // can be restored immediately instead of waiting for a new host registration.
            var staleIds = new List<SerializableGuid>();
            foreach (IInventoryModel model in Registered.Values.ToArray())
            {
                if (Pending.TryGetValue(model.Id, out InventorySnapshot snapshot))
                {
                    Restore(model, snapshot);
                    Pending.Remove(model.Id);
                }
                else if (pendingLegacyData != null && pendingLegacyData.Id == model.Id)
                {
                    RestoreLegacy(model, pendingLegacyData);
                    pendingLegacyData = null;
                }
                else
                {
                    // This model did not exist in the loaded save. Do not let newer
                    // unsaved contents leak into the older save state.
                    ClearModel(model);
                    staleIds.Add(model.Id);
                }
            }
            foreach (SerializableGuid id in staleIds)
                Registered.Remove(id);
        }

        public static void Reset()
        {
            Registered.Clear();
            Pending.Clear();
            pendingLegacyData = null;
        }

        private static InventorySnapshot Capture(IInventoryModel model)
        {
            var snapshot = new InventorySnapshot
            {
                InventoryId = model.Id,
                StorageKind = model is IGridInventoryModel ? "Grid" :
                    model is IEquipmentInventoryModel ? "Equipment" :
                    model is ISlotInventoryModel ? "Slots" : "Unknown"
            };

            foreach (Item item in model.Items)
            {
                if (item == null || item.details == null) continue;
                var entry = new InventoryItemSnapshot
                {
                    ItemId = item.Id,
                    ItemDefinitionId = item.detailsId,
                    Quantity = item.quantity
                };

                if (model is IGridInventoryModel grid && grid.TryGetPlacement(item.Id, out GridItemPlacement placement))
                {
                    entry.X = placement.X;
                    entry.Y = placement.Y;
                    entry.RotationQuarterTurns = placement.RotationQuarterTurns;
                }
                else if (model is IEquipmentInventoryModel equipment && equipment.TryGetSlotOf(item.Id, out string slotId))
                {
                    entry.SlotId = slotId;
                }
                else if (model is ISlotInventoryModel slots && slots.TryGetSlotOf(item.Id, out int slotIndex))
                {
                    entry.SlotIndex = slotIndex;
                }
                snapshot.Items.Add(entry);
            }
            return snapshot;
        }

        private static void Restore(IInventoryModel model, InventorySnapshot snapshot)
        {
            string actualKind = model is IGridInventoryModel ? "Grid" :
                model is IEquipmentInventoryModel ? "Equipment" :
                model is ISlotInventoryModel ? "Slots" : "Unknown";
            if (!string.IsNullOrEmpty(snapshot.StorageKind) && snapshot.StorageKind != actualKind)
                Debug.LogWarning($"Inventory {model.DisplayName}: saved storage type '{snapshot.StorageKind}' differs from current type '{actualKind}'. Items will be restored using the current model's placement rules.");
            ClearModel(model);
            if (snapshot.Items == null) return;

            foreach (InventoryItemSnapshot saved in snapshot.Items)
            {
                if (saved == null || saved.ItemId == SerializableGuid.Empty || saved.Quantity < 1) continue;
                if (!ItemDatabase.TryGetDetailsById(saved.ItemDefinitionId, out ItemDetails details))
                {
                    Debug.LogWarning($"Inventory {model.DisplayName}: item definition {saved.ItemDefinitionId} was not found; skipping saved item.");
                    continue;
                }

                int remaining = saved.Quantity;
                bool firstStack = true;
                while (remaining > 0)
                {
                    int stackQuantity = Mathf.Min(remaining, Mathf.Max(1, details.maxStack));
                    Item item = details.Create(stackQuantity);
                    if (firstStack) item.Id = saved.ItemId;
                    bool added = false;
                    InventoryOperationFailure failure = InventoryOperationFailure.None;

                    if (firstStack && model is IGridInventoryModel grid)
                    {
                        added = grid.TryAddAt(item, saved.X, saved.Y, saved.RotationQuarterTurns, out failure);
                        if (!added)
                        {
                            Debug.LogWarning($"Inventory {model.DisplayName}: couldn't restore '{details.Name}' at ({saved.X},{saved.Y}) rotation {saved.RotationQuarterTurns}; trying another valid position. Reason: {failure}.");
                            added = grid.TryAdd(item, out failure);
                        }
                    }
                    else if (model is IGridInventoryModel gridFallback)
                    {
                        added = gridFallback.TryAdd(item, out failure);
                    }
                    else if (firstStack && model is IEquipmentInventoryModel equipment)
                    {
                        added = !string.IsNullOrWhiteSpace(saved.SlotId) && equipment.TryAddAt(item, saved.SlotId, out failure);
                        if (!added) added = equipment.TryAdd(item, out failure);
                    }
                    else if (model is IEquipmentInventoryModel equipmentFallback)
                    {
                        added = equipmentFallback.TryAdd(item, out failure);
                    }
                    else if (firstStack && model is ISlotInventoryModel slots)
                    {
                        added = saved.SlotIndex >= 0 && slots.TryAddAt(item, saved.SlotIndex, out failure);
                        if (!added) added = slots.TryAdd(item, out failure);
                    }
                    else if (model is ISlotInventoryModel slotFallback)
                    {
                        added = slotFallback.TryAdd(item, out failure);
                    }
                    else
                    {
                        added = model.TryAdd(item, out failure);
                    }

                    if (!added)
                    {
                        Debug.LogWarning($"Inventory {model.DisplayName}: couldn't restore '{details.Name}' ({saved.ItemId}); {remaining} item(s) could not be restored. Reason: {failure}.");
                        break;
                    }
                    remaining -= stackQuantity;
                    firstStack = false;
                }
            }
        }

        private static void RestoreLegacy(IInventoryModel model, LegacyInventoryData legacy)
        {
            ClearModel(model);
            if (legacy.Items == null) return;

            for (int i = 0; i < legacy.Items.Length; i++)
            {
                Item oldItem = legacy.Items[i];
                if (oldItem == null || oldItem.quantity < 1) continue;
                SerializableGuid definitionId = oldItem.detailsId;
                if (!ItemDatabase.TryGetDetailsById(definitionId, out ItemDetails details))
                {
                    Debug.LogWarning($"Legacy inventory migration: item definition {definitionId} was not found; skipping item.");
                    continue;
                }

                int remaining = oldItem.quantity;
                bool firstStack = true;
                while (remaining > 0)
                {
                    int stackQuantity = Mathf.Min(remaining, Mathf.Max(1, details.maxStack));
                    Item migrated = details.Create(stackQuantity);
                    if (firstStack && oldItem.Id != SerializableGuid.Empty)
                        migrated.Id = oldItem.Id;

                    bool added = false;
                    InventoryOperationFailure failure = InventoryOperationFailure.None;
                    if (firstStack && model is ISlotInventoryModel slots)
                        added = slots.TryAddAt(migrated, i, out failure);
                    if (!added)
                        added = model.TryAdd(migrated, out failure);

                    if (!added)
                    {
                        Debug.LogWarning($"Legacy inventory migration: couldn't restore '{details.Name}' from old slot {i}. Reason: {failure}.");
                        break;
                    }
                    remaining -= stackQuantity;
                    firstStack = false;
                }
            }

            Debug.Log($"Migrated legacy inventory data into '{model.DisplayName}'. Save the game to write it in the new format.");
        }

        private static void ClearModel(IInventoryModel model)
        {
            foreach (Item item in model.Items.ToArray())
                if (item != null)
                    model.TryRemove(item.Id, out _, out _);
        }
    }
}
