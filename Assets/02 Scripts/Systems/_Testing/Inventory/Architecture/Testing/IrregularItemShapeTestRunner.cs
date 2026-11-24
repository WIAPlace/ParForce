using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Focused checks for custom item masks, empty cells, and rotated footprints.</summary>
    public sealed class IrregularItemShapeTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Irregular Item Shape Tests")]
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

            ItemDetails lShapeDefinition = CreateShape("L-shaped item", 2, 2,
                new List<bool> { true, false, true, true });
            ItemDetails singleDefinition = CreateShape("Single cell", 1, 1, new List<bool> { true });
            try
            {
                var grid = new GridInventoryModel(SerializableGuid.NewGuid(), "Shape Test Grid", 6, 5);
                Item lShape = lShapeDefinition.Create(1);
                bool added = grid.TryAddAt(lShape, 1, 1, 0, out _);
                bool occupiedCellsFound = grid.TryGetItemAtCell(1, 1, out Item topLeft) && topLeft.Id == lShape.Id &&
                    grid.TryGetItemAtCell(1, 2, out Item bottomLeft) && bottomLeft.Id == lShape.Id &&
                    grid.TryGetItemAtCell(2, 2, out Item bottomRight) && bottomRight.Id == lShape.Id;
                bool emptyCellAvailable = !grid.TryGetItemAtCell(2, 1, out _);
                Check("L-shaped item reserves only its painted cells", added && occupiedCellsFound && emptyCellAvailable);

                Item single = singleDefinition.Create(1);
                bool fitsInNotch = grid.TryAddAt(single, 2, 1, 0, out _);
                Item overlapProbe = singleDefinition.Create(1);
                bool occupiedCellBlocked = !grid.CanPlaceAt(overlapProbe, 1, 1, 0, out _);
                Check("Another item can occupy an empty cell inside the shape's bounding rectangle", fitsInNotch);
                Check("Occupied cells still reject overlapping items", occupiedCellBlocked);

                var rotatedGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Rotated Shape Grid", 5, 5);
                Item rotated = lShapeDefinition.Create(1);
                bool rotatedAdded = rotatedGrid.TryAddAt(rotated, 1, 1, 1, out _);
                bool clockwiseMask = rotatedAdded &&
                    rotatedGrid.TryGetItemAtCell(1, 1, out _) &&
                    rotatedGrid.TryGetItemAtCell(2, 1, out _) &&
                    rotatedGrid.TryGetItemAtCell(1, 2, out _) &&
                    !rotatedGrid.TryGetItemAtCell(2, 2, out _);
                Check("Clockwise rotation rotates occupied cells, not just the bounding box", clockwiseMask);

                Item notchOccupant = singleDefinition.Create(1);
                bool rotatedNotchAvailable = rotatedGrid.TryAddAt(notchOccupant, 2, 2, 0, out _);
                Check("90-degree shape leaves its rotated empty cell available", rotatedNotchAvailable);

                var halfTurnGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Half Turn Grid", 5, 5);
                Item halfTurn = lShapeDefinition.Create(1);
                bool halfTurnAdded = halfTurnGrid.TryAddAt(halfTurn, 1, 1, 2, out _);
                bool halfTurnMask = halfTurnAdded && halfTurnGrid.TryGetItemAtCell(1, 1, out _) &&
                    halfTurnGrid.TryGetItemAtCell(2, 1, out _) && halfTurnGrid.TryGetItemAtCell(2, 2, out _) &&
                    !halfTurnGrid.TryGetItemAtCell(1, 2, out _);
                Check("180-degree rotation preserves the correctly reversed cell mask", halfTurnMask);

                var threeQuarterGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Three Quarter Grid", 5, 5);
                Item threeQuarter = lShapeDefinition.Create(1);
                bool threeQuarterAdded = threeQuarterGrid.TryAddAt(threeQuarter, 1, 1, 3, out _);
                bool threeQuarterMask = threeQuarterAdded && threeQuarterGrid.TryGetItemAtCell(1, 2, out _) &&
                    threeQuarterGrid.TryGetItemAtCell(2, 2, out _) && threeQuarterGrid.TryGetItemAtCell(2, 1, out _) &&
                    !threeQuarterGrid.TryGetItemAtCell(1, 1, out _);
                Check("270-degree rotation preserves the correctly rotated cell mask", threeQuarterMask);
            }
            finally
            {
                Object.DestroyImmediate(lShapeDefinition);
                Object.DestroyImmediate(singleDefinition);
            }

            Debug.Log($"IRREGULAR ITEM SHAPE TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more irregular item shape tests failed.", this);
        }

        private static ItemDetails CreateShape(string itemName, int width, int height, List<bool> cells)
        {
            ItemDetails details = ScriptableObject.CreateInstance<ItemDetails>();
            details.Name = itemName;
            details.GridWidth = width;
            details.GridHeight = height;
            details.UseCustomShape = true;
            details.ShapeCells = cells;
            return details;
        }
    }
}
