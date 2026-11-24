using Systems.Inventory;
using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Isolated checks for stack limits, partial merges, quick move and equipment compatibility.</summary>
    public sealed class InventoryStackingQuickMoveTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Stacking and Quick Move Tests")]
        public void RunTests()
        {
            int passed = 0;
            int failed = 0;
            var report = new List<string>();
            void Check(string name, bool condition)
            {
                report.Add($"{(condition ? "PASS" : "FAIL")}: {name}");
                if (condition) passed++; else failed++;
            }

            ItemDetails potionDefinition = CreateDefinition("Test Potion", ItemCategory.General, 10);
            ItemDetails otherDefinition = CreateDefinition("Other Potion", ItemCategory.General, 10);
            ItemDetails helmetDefinition = CreateDefinition("Test Helmet", ItemCategory.Equipment, 1);
            ItemDetails longItemDefinition = CreateDefinition("Long Test Item", ItemCategory.General, 1);
            longItemDefinition.GridWidth = 2;
            longItemDefinition.GridHeight = 3;
            try
            {
                var source = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Source", 4);
                var firstDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Priority Destination", 1);
                var secondDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Fallback Destination", 2);
                Item sourceStack = potionDefinition.Create(10);
                Item partialStack = potionDefinition.Create(8);
                firstDestination.TryAddAt(partialStack, 0, out _);
                source.TryAddAt(sourceStack, 0, out _);

                InventoryQuickMoveResult partialMove = new InventoryQuickMoveService().QuickMove(source,
                    sourceStack.Id, new IInventoryModel[] { firstDestination, secondDestination });
                bool merged = firstDestination.TryGetItem(partialStack.Id, out Item mergedStack) && mergedStack.quantity == 10;
                bool fallbackCreated = secondDestination.ItemCount == 1 && secondDestination.Items[0].quantity == 8;
                Check("Quick move fills a partial stack, then continues to the next destination", partialMove.MovedQuantity == 10 && merged && fallbackCreated);
                Check("A fully moved source stack is removed without duplication", !source.TryGetItem(sourceStack.Id, out _) && firstDestination.ItemCount + secondDestination.ItemCount == 2);

                var mismatchSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Mismatch Source", 2);
                var mismatchDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Mismatch Destination", 2);
                Item mismatchItem = potionDefinition.Create(4);
                Item otherStack = otherDefinition.Create(6);
                mismatchSource.TryAddAt(mismatchItem, 0, out _);
                mismatchDestination.TryAddAt(otherStack, 0, out _);
                InventoryQuickMoveResult mismatchMove = new InventoryQuickMoveService().QuickMove(mismatchSource,
                    mismatchItem.Id, new IInventoryModel[] { mismatchDestination });
                Check("Different item definitions never merge", mismatchMove.Success && mismatchDestination.ItemCount == 2 && otherStack.quantity == 6);

                var fullSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Full Source", 2);
                var fullDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Full Destination", 1);
                Item retained = potionDefinition.Create(3);
                fullSource.TryAddAt(retained, 0, out _);
                fullDestination.TryAddAt(otherDefinition.Create(10), 0, out _);
                InventoryQuickMoveResult blockedMove = new InventoryQuickMoveService().QuickMove(fullSource,
                    retained.Id, new IInventoryModel[] { fullDestination });
                Check("A full incompatible destination leaves the source untouched", !blockedMove.Success &&
                    fullSource.TryGetItem(retained.Id, out Item stillThere) && stillThere.quantity == 3);

                var equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Equipment",
                    new[] { new EquipmentSlotDefinition("Head", "Head", new[] { EquipmentType.Head }) });
                var helmetSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Helmet Source", 2);
                Item helmet = helmetDefinition.Create(1);
                helmet.details.equipmentType = EquipmentType.Head;
                helmetSource.TryAddAt(helmet, 0, out _);
                InventoryQuickMoveResult equipmentMove = new InventoryQuickMoveService().QuickMove(helmetSource,
                    helmet.Id, new IInventoryModel[] { equipment });
                Check("Compatible equipment can be quick-moved into its equipment slot", equipmentMove.Success &&
                    equipment.TryGetItemAt("Head", out Item equipped) && equipped.Id == helmet.Id);

                var mergeSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Merge Source", 2);
                var mergeDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Merge Destination", 2);
                Item dragSource = potionDefinition.Create(5);
                Item dragTarget = potionDefinition.Create(7);
                mergeSource.TryAddAt(dragSource, 0, out _);
                mergeDestination.TryAddAt(dragTarget, 0, out _);
                InventoryQuickMoveResult directMerge = new InventoryQuickMoveService().TryMergeInto(
                    mergeSource, mergeDestination, dragSource.Id, dragTarget.Id);
                Check("Direct stack merge respects the maximum and leaves the remainder in the source", directMerge.MovedQuantity == 3 &&
                    dragTarget.quantity == 10 && mergeSource.TryGetItem(dragSource.Id, out Item remainder) && remainder.quantity == 2);

                var legacyMergeSource = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Legacy Merge Source", 1);
                var legacyMergeDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Legacy Merge Destination", 1);
                Item legacyOriginalStack = potionDefinition.Create(4);
                // Simulate a previously serialized runtime item whose cached definition ID is stale.
                legacyOriginalStack.detailsId = SerializableGuid.Empty;
                Item newlyCreatedStack = potionDefinition.Create(3);
                legacyMergeSource.TryAddAt(newlyCreatedStack, 0, out _);
                legacyMergeDestination.TryAddAt(legacyOriginalStack, 0, out _);
                InventoryQuickMoveResult legacyMerge = new InventoryQuickMoveService().TryMergeInto(
                    legacyMergeSource, legacyMergeDestination, newlyCreatedStack.Id, legacyOriginalStack.Id);
                Check("Existing stacks still accept matching items when their cached definition ID is stale",
                    legacyMerge.MovedQuantity == 3 && legacyOriginalStack.quantity == 7 &&
                    !legacyMergeSource.TryGetItem(newlyCreatedStack.Id, out _));

                var rotationGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Rotation Grid", 8, 8);
                Item rotatingItem = longItemDefinition.Create(1);
                bool rotationAdded = rotationGrid.TryAddAt(rotatingItem, 0, 0, 0, out _);
                bool rotation0 = rotationAdded && rotationGrid.TryGetPlacement(rotatingItem.Id, out GridItemPlacement at0) &&
                    at0.RotationQuarterTurns == 0 && at0.RotationDegrees == 0 && at0.Width == 2 && at0.Height == 3;
                bool rotation1 = rotationGrid.TryMoveTo(rotatingItem.Id, 4, 0, 1, out _) &&
                    rotationGrid.TryGetPlacement(rotatingItem.Id, out GridItemPlacement at1) &&
                    at1.RotationQuarterTurns == 1 && at1.RotationDegrees == 90 && at1.Width == 3 && at1.Height == 2;
                bool rotation2 = rotationGrid.TryMoveTo(rotatingItem.Id, 0, 4, 2, out _) &&
                    rotationGrid.TryGetPlacement(rotatingItem.Id, out GridItemPlacement at2) &&
                    at2.RotationQuarterTurns == 2 && at2.RotationDegrees == 180 && at2.Width == 2 && at2.Height == 3;
                bool rotation3 = rotationGrid.TryMoveTo(rotatingItem.Id, 4, 4, 3, out _) &&
                    rotationGrid.TryGetPlacement(rotatingItem.Id, out GridItemPlacement at3) &&
                    at3.RotationQuarterTurns == 3 && at3.RotationDegrees == 270 && at3.Width == 3 && at3.Height == 2;
                Check("Grid model stores all four rotation states and swaps footprint only at 90/270 degrees",
                    rotation0 && rotation1 && rotation2 && rotation3);
            }
            finally
            {
                Object.DestroyImmediate(potionDefinition);
                Object.DestroyImmediate(otherDefinition);
                Object.DestroyImmediate(helmetDefinition);
                Object.DestroyImmediate(longItemDefinition);
            }

            Debug.Log($"STACKING / QUICK MOVE TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more stacking / quick move tests failed.", this);
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
