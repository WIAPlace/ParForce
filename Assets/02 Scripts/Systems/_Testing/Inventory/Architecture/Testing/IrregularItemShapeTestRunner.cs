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
                bool added = grid.AddAt(lShape, 1, 1, 0).Success;
                bool occupiedCellsFound = grid.TryGetItemAtCell(1, 1, out Item topLeft) && topLeft.Id == lShape.Id &&
                    grid.TryGetItemAtCell(1, 2, out Item bottomLeft) && bottomLeft.Id == lShape.Id &&
                    grid.TryGetItemAtCell(2, 2, out Item bottomRight) && bottomRight.Id == lShape.Id;
                bool emptyCellAvailable = !grid.TryGetItemAtCell(2, 1, out _);
                Check("L-shaped item reserves only its painted cells", added && occupiedCellsFound && emptyCellAvailable);

                Item single = singleDefinition.Create(1);
                bool fitsInNotch = grid.AddAt(single, 2, 1, 0).Success;
                Item overlapProbe = singleDefinition.Create(1);
                bool occupiedCellBlocked = !grid.CanPlaceAt(overlapProbe, 1, 1, 0, out _);
                Check("Another item can occupy an empty cell inside the shape's bounding rectangle", fitsInNotch);
                Check("Occupied cells still reject overlapping items", occupiedCellBlocked);

                var rotatedGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Rotated Shape Grid", 5, 5);
                Item rotated = lShapeDefinition.Create(1);
                bool rotatedAdded = rotatedGrid.AddAt(rotated, 1, 1, 1).Success;
                bool clockwiseMask = rotatedAdded &&
                    rotatedGrid.TryGetItemAtCell(1, 1, out _) &&
                    rotatedGrid.TryGetItemAtCell(2, 1, out _) &&
                    rotatedGrid.TryGetItemAtCell(1, 2, out _) &&
                    !rotatedGrid.TryGetItemAtCell(2, 2, out _);
                Check("Clockwise rotation rotates occupied cells, not just the bounding box", clockwiseMask);

                Item notchOccupant = singleDefinition.Create(1);
                bool rotatedNotchAvailable = rotatedGrid.AddAt(notchOccupant, 2, 2, 0).Success;
                Check("90-degree shape leaves its rotated empty cell available", rotatedNotchAvailable);

                var halfTurnGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Half Turn Grid", 5, 5);
                Item halfTurn = lShapeDefinition.Create(1);
                bool halfTurnAdded = halfTurnGrid.AddAt(halfTurn, 1, 1, 2).Success;
                bool halfTurnMask = halfTurnAdded && halfTurnGrid.TryGetItemAtCell(1, 1, out _) &&
                    halfTurnGrid.TryGetItemAtCell(2, 1, out _) && halfTurnGrid.TryGetItemAtCell(2, 2, out _) &&
                    !halfTurnGrid.TryGetItemAtCell(1, 2, out _);
                Check("180-degree rotation preserves the correctly reversed cell mask", halfTurnMask);

                var threeQuarterGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "Three Quarter Grid", 5, 5);
                Item threeQuarter = lShapeDefinition.Create(1);
                bool threeQuarterAdded = threeQuarterGrid.AddAt(threeQuarter, 1, 1, 3).Success;
                bool threeQuarterMask = threeQuarterAdded && threeQuarterGrid.TryGetItemAtCell(1, 2, out _) &&
                    threeQuarterGrid.TryGetItemAtCell(2, 2, out _) && threeQuarterGrid.TryGetItemAtCell(2, 1, out _) &&
                    !threeQuarterGrid.TryGetItemAtCell(1, 1, out _);
                Check("270-degree rotation preserves the correctly rotated cell mask", threeQuarterMask);

                ItemDetails paddedSingleDefinition = CreateShape("Padded single cell", 3, 3,
                    new List<bool> { false, false, false, false, true, false, false, false, false });
                try
                {
                    Item paddedSingle = paddedSingleDefinition.Create(1);
                    var oneCellGrid = new GridInventoryModel(SerializableGuid.NewGuid(), "One Cell Auto Fit", 1, 1);
                    bool autoPlaced = oneCellGrid.Add(paddedSingle).Success &&
                        oneCellGrid.TryGetPlacement(paddedSingle.Id, out GridItemPlacement paddedPlacement) &&
                        paddedPlacement.Width == 1 && paddedPlacement.Height == 1 &&
                        oneCellGrid.TryGetItemAtCell(0, 0, out Item placedPadded) && placedPadded.Id == paddedSingle.Id;
                    Check("A 3x3 shape with one occupied center cell auto-fits a 1x1 inventory", autoPlaced);

                    ItemGridShape.GetRotatedSize(paddedSingleDefinition, 1, out int trimmedRotatedWidth, out int trimmedRotatedHeight);
                    bool trimmedRotation = trimmedRotatedWidth == 1 && trimmedRotatedHeight == 1 &&
                        ItemGridShape.IsOccupied(paddedSingleDefinition, 0, 0, 1);
                    Check("Trimming empty padding preserves a 1x1 footprint in every rotation", trimmedRotation);

                    ItemDetails paddedL = CreateShape("Padded L", 4, 3, new List<bool>
                    {
                        false, true, false, false,
                        false, true, true, false,
                        false, false, false, false
                    });
                    try
                    {
                        ItemGridShape.GetRotatedSize(paddedL, 0, out int trimmedWidth, out int trimmedHeight);
                        bool normalizedBounds = trimmedWidth == 2 && trimmedHeight == 2 &&
                            ItemGridShape.IsOccupied(paddedL, 0, 0, 0) &&
                            ItemGridShape.IsOccupied(paddedL, 0, 1, 0) &&
                            ItemGridShape.IsOccupied(paddedL, 1, 1, 0) &&
                            !ItemGridShape.IsOccupied(paddedL, 1, 0, 0);
                        ItemGridShape.GetRotatedSize(paddedL, 1, out int rotatedTrimmedWidth, out int rotatedTrimmedHeight);
                        bool normalizedRotation = rotatedTrimmedWidth == 2 && rotatedTrimmedHeight == 2 &&
                            ItemGridShape.IsOccupied(paddedL, 1, 0, 1) &&
                            ItemGridShape.IsOccupied(paddedL, 0, 0, 1) &&
                            ItemGridShape.IsOccupied(paddedL, 0, 1, 1) &&
                            !ItemGridShape.IsOccupied(paddedL, 1, 1, 1);
                        Check("Padded irregular shapes normalize to their occupied bounds", normalizedBounds);
                        Check("Padded irregular shapes rotate correctly after normalization", normalizedRotation);
                    }
                    finally
                    {
                        Object.DestroyImmediate(paddedL);
                    }

                    float automaticIconSize = ItemGridShape.GetIconSizeInCells(paddedSingleDefinition);
                    paddedSingleDefinition.IconSizeInCells = 2.5f;
                    float configuredIconSize = ItemGridShape.GetIconSizeInCells(paddedSingleDefinition);
                    Check("Icon size defaults to the longest occupied dimension and can be uniformly configured",
                        Mathf.Approximately(automaticIconSize, 1f) && Mathf.Approximately(configuredIconSize, 2.5f));
                }
                finally
                {
                    Object.DestroyImmediate(paddedSingleDefinition);
                }
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
