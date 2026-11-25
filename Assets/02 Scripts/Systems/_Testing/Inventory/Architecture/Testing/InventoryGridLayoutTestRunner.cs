using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Model-level checks for designer-authored usable and blocked grid cells.</summary>
    public sealed class InventoryGridLayoutTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Inventory Grid Layout Tests")]
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

            ItemDetails lShapeDefinition = ScriptableObject.CreateInstance<ItemDetails>();
            ItemDetails singleDefinition = ScriptableObject.CreateInstance<ItemDetails>();
            InventoryGridLayout dynamicLayout = ScriptableObject.CreateInstance<InventoryGridLayout>();
            try
            {
                lShapeDefinition.Name = "Grid Layout L Shape";
                lShapeDefinition.GridWidth = 2;
                lShapeDefinition.GridHeight = 2;
                lShapeDefinition.UseCustomShape = true;
                lShapeDefinition.ShapeCells = new List<bool> { true, false, true, true };
                singleDefinition.Name = "Grid Layout Single Cell";
                singleDefinition.GridWidth = 1;
                singleDefinition.GridHeight = 1;
                singleDefinition.maxStack = 10;

                // Row-major mask: all cells usable except bottom-right.
                bool[] mask =
                {
                    true, true, true,
                    true, true, true,
                    true, true, false
                };
                var grid = new GridInventoryModel(SerializableGuid.NewGuid(), "Masked Grid", 3, 3, null, mask);
                Check("Capacity counts only usable cells", grid.Capacity == 8);
                Check("Usable and blocked cells are reported correctly", grid.IsCellUsable(0, 0) && !grid.IsCellUsable(2, 2));
                Check("Empty grid reports all usable cells available",
                    grid.TotalUsableCells == 8 && grid.OccupiedCells == 0 &&
                    grid.AvailableCells == 8 && Mathf.Approximately(grid.FillPercentage, 0f));

                Item lShape = lShapeDefinition.Create(1);
                bool lShapeFits = grid.AddAt(lShape, 0, 0, 0).Success;
                Check("An irregular item can occupy usable cells around its empty notch", lShapeFits);

                Item notchItem = singleDefinition.Create(1);
                bool notchFits = grid.AddAt(notchItem, 1, 0, 0).Success;
                Check("An item can still occupy an unblocked empty notch in another item's bounds", notchFits);
                Check("Occupancy counts actual irregular shape cells, not bounding rectangles",
                    grid.OccupiedCells == 4 && grid.AvailableCells == 4 &&
                    Mathf.Approximately(grid.FillPercentage, 50f));

                Item blockedCellItem = singleDefinition.Create(1);
                bool blockedRejected = !grid.CanPlaceAt(blockedCellItem, 2, 2, 0, out _);
                Check("Items are rejected when an occupied footprint cell overlaps blocked space", blockedRejected);

                Item autoPlaced = singleDefinition.Create(1);
                bool autoAdd = grid.Add(autoPlaced).Success;
                bool autoAvoidedBlocked = autoAdd && grid.TryGetPlacement(autoPlaced.Id, out GridItemPlacement autoPlacement) &&
                    grid.IsCellUsable(autoPlacement.X, autoPlacement.Y);
                Check("Automatic placement only selects usable cells", autoAvoidedBlocked);
                Check("Occupancy updates after adding an item",
                    grid.OccupiedCells == 5 && grid.AvailableCells == 3 &&
                    Mathf.Approximately(grid.FillPercentage, 62.5f));
                bool quantityChanged = grid.SetQuantity(autoPlaced.Id, 2).Success;
                Check("Changing stack quantity does not change occupied grid cells",
                    quantityChanged && autoPlaced.quantity == 2 && grid.OccupiedCells == 5);
                bool removedAutoPlaced = grid.Remove(autoPlaced.Id).Success;
                Check("Occupancy updates after removing an item", removedAutoPlaced &&
                    grid.OccupiedCells == 4 && grid.AvailableCells == 4 &&
                    Mathf.Approximately(grid.FillPercentage, 50f));

                bool blockedCellHasNoItem = !grid.TryGetItemAtCell(2, 2, out _);
                Check("Blocked cells never resolve to an item", blockedCellHasNoItem);

                bool[] allBlocked = new bool[4];
                var emptyGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "No Usable Cells", 2, 2, null, allBlocked);
                Item cannotFit = singleDefinition.Create(1);
                InventoryOperationResult rejectedAdd = emptyGrid.Add(cannotFit);
                bool rejectsAll = !rejectedAdd.Success &&
                    rejectedAdd.Failure == InventoryOperationFailure.InventoryFull;
                Check("A layout with no usable cells safely rejects storage", rejectsAll && emptyGrid.Capacity == 0);
                Check("A grid with no usable cells reports zero fill without division errors",
                    emptyGrid.TotalUsableCells == 0 && emptyGrid.OccupiedCells == 0 &&
                    emptyGrid.AvailableCells == 0 && Mathf.Approximately(emptyGrid.FillPercentage, 0f));

                var dynamicGrid = new GridInventoryModel(SerializableGuid.NewGuid(),
                    "Dynamic Asset Grid", dynamicLayout);
                Check("A dynamic grid model adopts its layout dimensions and usable-cell mask",
                    dynamicGrid.Width == dynamicLayout.Width && dynamicGrid.Height == dynamicLayout.Height &&
                    dynamicGrid.Capacity == dynamicLayout.UsableCellCount &&
                    dynamicGrid.IsCellUsable(0, 0) == dynamicLayout.IsCellUsable(0, 0));
            }
            finally
            {
                Object.DestroyImmediate(lShapeDefinition);
                Object.DestroyImmediate(singleDefinition);
                Object.DestroyImmediate(dynamicLayout);
            }

            Debug.Log($"INVENTORY GRID LAYOUT TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more inventory grid layout tests failed.", this);
        }
    }
}
