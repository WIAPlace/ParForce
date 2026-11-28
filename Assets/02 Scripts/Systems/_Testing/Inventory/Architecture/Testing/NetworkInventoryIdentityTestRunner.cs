using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture.Networking;
using Systems.Inventory.Architecture.Persistence;
using UnityEngine;

namespace Systems.Inventory.Architecture.Testing
{
    /// <summary>Small runtime smoke tests for compact network ID mapping and snapshot conversion.</summary>
    public sealed class NetworkInventoryIdentityTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Network Inventory Identity Tests")]
        public void RunTests()
        {
            int passed = 0;
            int failed = 0;
            ItemDetails sword = ScriptableObject.CreateInstance<ItemDetails>();
            ItemDetails potion = ScriptableObject.CreateInstance<ItemDetails>();
            ItemDetails duplicate = null;

            try
            {
                sword.name = "Network Test Sword";
                sword.Id = SerializableGuid.NewGuid();
                sword.NetworkDefinitionId = 101;
                potion.name = "Network Test Potion";
                potion.Id = SerializableGuid.NewGuid();
                potion.NetworkDefinitionId = 202;

                bool catalogCreated = NetworkItemDefinitionRegistry.TryCreate(
                    new[] { sword, potion }, out NetworkItemDefinitionRegistry definitions, out List<string> catalogErrors);
                Check(catalogCreated, "Definition registry accepts unique non-zero IDs", ref passed, ref failed,
                    catalogCreated ? null : string.Join("; ", catalogErrors));
                Check(definitions != null && definitions.TryGetNetworkId(sword.Id, out ushort swordId) && swordId == 101,
                    "Definition GUID maps to compact ushort ID", ref passed, ref failed);
                Check(definitions != null && definitions.TryGetDefinition(202, out ItemDetails resolvedPotion) && resolvedPotion == potion,
                    "Compact ushort ID resolves to ItemDetails", ref passed, ref failed);

                duplicate = CreateDuplicateDefinition(sword);
                bool duplicateRejected = !NetworkItemDefinitionRegistry.TryCreate(
                    new[] { sword, potion, duplicate }, out _, out _);
                Check(duplicateRejected, "Duplicate network definition IDs are rejected", ref passed, ref failed);

                var instanceIds = new NetworkItemIdRegistry();
                SerializableGuid localOne = SerializableGuid.NewGuid();
                SerializableGuid localTwo = SerializableGuid.NewGuid();
                bool firstAssigned = instanceIds.TryGetOrAssign(localOne, out uint networkOne);
                bool secondAssigned = instanceIds.TryGetOrAssign(localTwo, out uint networkTwo);
                Check(firstAssigned && secondAssigned && networkOne != 0 && networkTwo != 0 && networkOne != networkTwo,
                    "Local item GUIDs receive distinct non-zero uint IDs", ref passed, ref failed);
                Check(instanceIds.TryGetOrAssign(localOne, out uint sameNetworkOne) && sameNetworkOne == networkOne,
                    "Repeated lookup preserves an item's network ID", ref passed, ref failed);

                var receiverIds = new NetworkItemIdRegistry();
                bool mappingRegistered = receiverIds.TryRegister(localOne, networkOne);
                bool conflictRejected = !receiverIds.TryRegister(localTwo, networkOne);
                Check(mappingRegistered && conflictRejected, "Receiver accepts matching mapping and rejects collisions", ref passed, ref failed);
                bool reverseResolved = receiverIds.TryGetLocalId(networkOne, out SerializableGuid resolvedLocal) && resolvedLocal == localOne;
                Check(reverseResolved, "Network item ID resolves back to local GUID", ref passed, ref failed);

                var gridSnapshot = new InventorySnapshot
                {
                    InventoryId = SerializableGuid.NewGuid(),
                    StorageKind = "Grid",
                    Items = new List<InventoryItemSnapshot>
                    {
                        new InventoryItemSnapshot
                        {
                            ItemId = SerializableGuid.NewGuid(),
                            ItemDefinitionId = sword.Id,
                            Quantity = 2,
                            X = 3,
                            Y = 1,
                            RotationQuarterTurns = 1
                        }
                    }
                };

                bool gridPacked = NetworkInventoryData.TryCreateFromSnapshot(
                    gridSnapshot, definitions, new NetworkItemIdRegistry(), 7,
                    out NetworkInventoryData networkGrid, out string gridError);
                Check(gridPacked && networkGrid.Count == 1 && networkGrid.Items[0].DefinitionId == 101 &&
                    networkGrid.Items[0].Quantity == 2 && networkGrid.Items[0].X == 3 &&
                    networkGrid.Items[0].Y == 1 && networkGrid.Items[0].RotationQuarterTurns == 1,
                    "Grid snapshot converts to compact network data", ref passed, ref failed, gridError);

                var gridReceiverIds = new NetworkItemIdRegistry();
                bool gridUnpacked = networkGrid.TryCreateSnapshot(SerializableGuid.NewGuid(), definitions,
                    gridReceiverIds, out InventorySnapshot restoredGrid, out string unpackError);
                Check(gridUnpacked && restoredGrid.Items.Count == 1 && restoredGrid.Items[0].ItemId != SerializableGuid.Empty &&
                    restoredGrid.Items[0].ItemDefinitionId == sword.Id && restoredGrid.Items[0].X == 3 &&
                    restoredGrid.Items[0].Y == 1 && restoredGrid.Items[0].RotationQuarterTurns == 1,
                    "Compact grid data converts back to local snapshot without transmitting GUIDs", ref passed, ref failed, unpackError);

                var slotDefinitions = new List<EquipmentSlotDefinition>
                {
                    new EquipmentSlotDefinition("MainHand", "Main Hand", new[] { EquipmentType.MainHand }, 10),
                    new EquipmentSlotDefinition("OffHand", "Off Hand", new[] { EquipmentType.OffHand }, 20)
                };
                bool slotsCreated = NetworkEquipmentSlotRegistry.TryCreate(slotDefinitions,
                    out NetworkEquipmentSlotRegistry slotRegistry, out List<string> slotErrors);
                string slotError = slotErrors == null || slotErrors.Count == 0
                    ? null
                    : string.Join("; ", slotErrors);
                Check(slotsCreated && slotRegistry.TryGetNetworkSlotId("OffHand", out ushort offHandNetworkId) && offHandNetworkId == 20,
                    "Equipment slot string maps to compact layout ID", ref passed, ref failed, slotError);

                var equipmentSnapshot = new InventorySnapshot
                {
                    InventoryId = SerializableGuid.NewGuid(),
                    StorageKind = "Equipment",
                    Items = new List<InventoryItemSnapshot>
                    {
                        new InventoryItemSnapshot
                        {
                            ItemId = SerializableGuid.NewGuid(),
                            ItemDefinitionId = sword.Id,
                            Quantity = 1,
                            SlotId = "MainHand"
                        }
                    }
                };
                NetworkEquipmentData networkEquipment = default;
                string equipmentError = slotsCreated ? null : slotError;
                bool equipmentPacked = slotsCreated && NetworkEquipmentData.TryCreateFromSnapshot(
                    equipmentSnapshot, definitions, new NetworkItemIdRegistry(), slotRegistry, 9,
                    out networkEquipment, out equipmentError);
                Check(equipmentPacked && networkEquipment.Count == 1 && networkEquipment.Items[0].NetworkSlotId == 10,
                    "Equipment snapshot converts to compact network data", ref passed, ref failed,
                    slotsCreated ? equipmentError : slotError);

                if (equipmentPacked)
                {
                    bool equipmentUnpacked = networkEquipment.TryCreateSnapshot(SerializableGuid.NewGuid(),
                        definitions, new NetworkItemIdRegistry(), slotRegistry,
                        out InventorySnapshot restoredEquipment, out string restoreEquipmentError);
                    Check(equipmentUnpacked && restoredEquipment.Items.Count == 1 &&
                        restoredEquipment.Items[0].SlotId == "MainHand" &&
                        restoredEquipment.Items[0].ItemDefinitionId == sword.Id,
                        "Compact equipment data converts back to local snapshot", ref passed, ref failed,
                        restoreEquipmentError);
                }
            }
            finally
            {
                Destroy(sword);
                Destroy(potion);
                if (duplicate != null) Destroy(duplicate);
            }

            Debug.Log($"NETWORK INVENTORY IDENTITY TESTS: {passed}/{passed + failed} PASSED, {failed} FAILED.", this);
        }

        private static ItemDetails CreateDuplicateDefinition(ItemDetails source)
        {
            ItemDetails duplicate = ScriptableObject.CreateInstance<ItemDetails>();
            duplicate.name = "Duplicate Network Test Definition";
            duplicate.Id = SerializableGuid.NewGuid();
            duplicate.NetworkDefinitionId = source.NetworkDefinitionId;
            return duplicate;
        }

        private void Check(bool condition, string description, ref int passed, ref int failed, string detail = null)
        {
            if (condition)
            {
                passed++;
                Debug.Log("PASS: " + description, this);
            }
            else
            {
                failed++;
                Debug.LogError("FAIL: " + description + (string.IsNullOrEmpty(detail) ? string.Empty : " — " + detail), this);
            }
        }
    }
}
