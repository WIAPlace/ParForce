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
                grid.AddAt(first, 0, 0, false);
                grid.AddAt(second, 2, 0, false);

                InventoryOperationResult instanceRemoval = grid.RemoveQuantityUpTo(first.Id, 3);
                bool byInstance = instanceRemoval.Success && instanceRemoval.AffectedQuantity == 3 && first.quantity == 5;
                Check("Remove a requested quantity from a specific item instance", byInstance);

                InventoryOperationResult itemRemoval = grid.RemoveQuantityUpTo(first, 1);
                Check("Remove quantity through a runtime Item reference",
                    itemRemoval.Success && itemRemoval.AffectedQuantity == 1 && first.quantity == 4);

                InventoryOperationResult definitionRemoval = grid.RemoveQuantityUpTo(arrow, 6);
                bool byDefinition = definitionRemoval.Success && definitionRemoval.AffectedQuantity == 6 &&
                    definitionRemoval.ItemChanges.Count == 2 && !grid.TryGetItem(first.Id, out _) && second.quantity == 5;
                Check("Remove a quantity across matching stacks by item definition", byDefinition);

                InventoryOperationResult indexRemoval = grid.RemoveQuantityAtIndex(0, 1);
                Check("Remove quantity by item-list index", indexRemoval.Success &&
                    indexRemoval.AffectedQuantity == 1 && second.quantity == 4);

                InventoryOperationResult cellRemoval = grid.RemoveQuantityAtCell(2, 0, 1);
                bool byCell = cellRemoval.Success && cellRemoval.AffectedQuantity == 1 && second.quantity == 3;
                Check("Remove quantity by grid cell, including irregular footprint lookup", byCell);

                InventoryOperationResult insufficient = grid.RemoveQuantityExact(arrow, 99);
                bool exactRejected = !insufficient.Success && insufficient.Failure == InventoryOperationFailure.InsufficientQuantity && second.quantity == 3;
                Check("Exact removal is all-or-nothing when quantity is insufficient", exactRejected);

                InventoryOperationResult exactRemoval = grid.RemoveQuantityExact(arrow, 3);
                bool exactAccepted = exactRemoval.Success && exactRemoval.AffectedQuantity == 3 && grid.ItemCount == 0;
                Check("Exact removal succeeds when the full quantity is available", exactAccepted);

                grid.AddAt(arrow.Create(2), 0, 0, false);
                grid.AddAt(arrow.Create(3), 2, 0, false);
                InventoryOperationResult allRemoval = grid.RemoveAll(arrow);
                bool removeAll = allRemoval.Success && allRemoval.AffectedQuantity == 5 && grid.ItemCount == 0;
                Check("Remove all remaining stacks matching a definition", removeAll);

                var equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Equipment",
                    new[] { new EquipmentSlotDefinition("MainHand", "Main Hand", new[] { EquipmentType.MainHand }) });
                Item knifeStack = knife.Create(4);
                bool equippedStack = equipment.AddAt(knifeStack, "MainHand").Success &&
                    equipment.TryGetItemAt("MainHand", out Item equipped) && equipped.quantity == 4;
                Check("Equipment slot accepts a stack when its definition maxStack is greater than one", equippedStack);

                InventoryOperationResult oneKnife = equipment.RemoveQuantityAt("MainHand", 1);
                bool consumeOne = oneKnife.Success && oneKnife.AffectedQuantity == 1 &&
                    knifeStack.quantity == 3 && equipment.OccupiedEquipmentSlots == 1;
                Check("Consuming one equipped throwing knife leaves the equipment slot occupied", consumeOne);

                InventoryOperationResult remainingKnives = equipment.RemoveQuantityUpTo(knife, 10);
                bool consumeRemaining = remainingKnives.Success && remainingKnives.AffectedQuantity == 3 &&
                    equipment.OccupiedEquipmentSlots == 0;
                Check("Removing more than the equipped stack contains removes the remainder and unequips it", consumeRemaining);

                var mergeEquipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Merge Equipment",
                    new[] { new EquipmentSlotDefinition("MainHand", "Main Hand", new[] { EquipmentType.MainHand }) });
                Item equippedKnives = knife.Create(3);
                mergeEquipment.AddAt(equippedKnives, "MainHand");
                var knifeSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Knife Source", 1);
                Item incomingKnives = knife.Create(4);
                knifeSource.AddAt(incomingKnives, 0);
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
