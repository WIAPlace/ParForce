using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Checks model-level quantity removal and stackable equipment, without requiring an open panel.</summary>
    public sealed class InventoryQuantityRemovalTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Quantity Removal and Stackable Equipment Tests")]
        public void RunTests()
        {
            int passed = 0, failed = 0;
            var report = new List<string>();
            void Check(string name, bool condition)
            {
                report.Add($"{(condition ? "PASS" : "FAIL")}: {name}");
                if (condition) passed++; else failed++;
            }

            ItemDetails arrow = CreateDefinition("Test Arrow", ItemCategory.General, 20);
            ItemDetails knife = CreateDefinition("Test Throwing Knife", ItemCategory.Equipment, 5);
            knife.equipmentType = EquipmentType.MainHand;
            try
            {
                var grid = new GridInventoryModel(SerializableGuid.NewGuid(), "Closed Grid", 6, 4);
                Item first = arrow.Create(8);
                Item second = arrow.Create(7);
                grid.TryAddAt(first, 0, 0, false, out _);
                grid.TryAddAt(second, 2, 0, false, out _);

                bool byInstance = grid.TryRemoveQuantity(first, 3, out int instanceRemoved, out _) &&
                    instanceRemoved == 3 && first.quantity == 5;
                Check("Remove a requested quantity from a specific item instance", byInstance);

                bool byDefinition = grid.TryRemoveQuantity(arrow, 6, out int definitionRemoved, out _) &&
                    definitionRemoved == 6 && !grid.TryGetItem(first.Id, out _) && second.quantity == 6;
                Check("Remove a quantity across matching stacks by item definition", byDefinition);

                bool byIndex = grid.TryRemoveQuantityAtCell(2, 0, 2, out int indexRemoved, out _) &&
                    indexRemoved == 2 && second.quantity == 4;
                Check("Remove quantity by grid cell, including irregular footprint lookup", byIndex);

                bool exactRejected = !grid.TryRemoveExact(arrow, 99, out _) && second.quantity == 4;
                Check("Exact removal is all-or-nothing when quantity is insufficient", exactRejected);

                bool exactAccepted = grid.TryRemoveExact(arrow, 4, out _) && grid.ItemCount == 0;
                Check("Exact removal succeeds when the full quantity is available", exactAccepted);

                grid.TryAddAt(arrow.Create(2), 0, 0, false, out _);
                grid.TryAddAt(arrow.Create(3), 2, 0, false, out _);
                bool removeAll = grid.TryRemoveAll(arrow, out int allRemoved, out _) && allRemoved == 5 && grid.ItemCount == 0;
                Check("Remove all remaining stacks matching a definition", removeAll);

                var equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Equipment",
                    new[] { new EquipmentSlotDefinition("MainHand", "Main Hand", new[] { EquipmentType.MainHand }) });
                Item knifeStack = knife.Create(4);
                bool equippedStack = equipment.TryAddAt(knifeStack, "MainHand", out _) &&
                    equipment.TryGetItemAt("MainHand", out Item equipped) && equipped.quantity == 4;
                Check("Equipment slot accepts a stack when its definition maxStack is greater than one", equippedStack);

                bool consumeOne = equipment.TryRemoveQuantityAt("MainHand", 1, out int knifeRemoved, out _) &&
                    knifeRemoved == 1 && knifeStack.quantity == 3 && equipment.OccupiedEquipmentSlots == 1;
                Check("Consuming one equipped throwing knife leaves the equipment slot occupied", consumeOne);

                bool consumeRemaining = equipment.TryRemoveQuantity(knife, 10, out int remainingRemoved, out _) &&
                    remainingRemoved == 3 && equipment.OccupiedEquipmentSlots == 0;
                Check("Removing more than the equipped stack contains removes the remainder and unequips it", consumeRemaining);

                var mergeEquipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Merge Equipment",
                    new[] { new EquipmentSlotDefinition("MainHand", "Main Hand", new[] { EquipmentType.MainHand }) });
                Item equippedKnives = knife.Create(3);
                mergeEquipment.TryAddAt(equippedKnives, "MainHand", out _);
                var knifeSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Knife Source", 1);
                Item incomingKnives = knife.Create(4);
                knifeSource.TryAddAt(incomingKnives, 0, out _);
                InventoryQuickMoveResult equipmentMerge = new InventoryQuickMoveService().QuickMove(
                    knifeSource, incomingKnives.Id, new IInventoryModel[] { mergeEquipment });
                Check("Quick move merges stackable equipment into an occupied compatible equipment slot",
                    equipmentMerge.MovedQuantity == 2 && equippedKnives.quantity == 5 &&
                    knifeSource.TryGetItem(incomingKnives.Id, out Item knifeRemainder) && knifeRemainder.quantity == 2);
            }
            finally
            {
                Object.DestroyImmediate(arrow);
                Object.DestroyImmediate(knife);
            }

            Debug.Log($"QUANTITY REMOVAL TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more quantity removal tests failed.", this);
        }

        private static ItemDetails CreateDefinition(string itemName, ItemCategory category, int maxStack)
        {
            ItemDetails definition = ScriptableObject.CreateInstance<ItemDetails>();
            definition.Name = itemName;
            definition.category = category;
            definition.maxStack = maxStack;
            return definition;
        }
    }
}
