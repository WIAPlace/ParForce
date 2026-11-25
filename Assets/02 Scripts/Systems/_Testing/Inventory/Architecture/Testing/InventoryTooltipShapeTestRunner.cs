using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Validates the footprint data exposed to tooltip visual sections.</summary>
    public sealed class InventoryTooltipShapeTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Inventory Tooltip Shape Tests")]
        public void RunTests()
        {
            int passed = 0;
            int failed = 0;
            ItemDetails details = ScriptableObject.CreateInstance<ItemDetails>();
            try
            {
                details.Name = "Tooltip L Shape";
                details.GridWidth = 2;
                details.GridHeight = 2;
                details.UseCustomShape = true;
                details.ShapeCells = new List<bool> { true, false, true, true };
                Item item = details.Create(3);

                InventoryItemTooltipContext normal = new InventoryItemTooltipContext(item, 0);
                Check("Tooltip context reports irregular occupied-cell count", normal.OccupiedCellCount == 3, ref passed, ref failed);
                Check("Tooltip context reports unrotated footprint", normal.FootprintWidth == 2 && normal.FootprintHeight == 2,
                    ref passed, ref failed);

                InventoryItemTooltipContext rotated = new InventoryItemTooltipContext(item, 1);
                Check("Tooltip context reports rotated footprint", rotated.FootprintWidth == 2 && rotated.FootprintHeight == 2,
                    ref passed, ref failed);
                Check("Tooltip context preserves occupied-cell count after rotation", rotated.OccupiedCellCount == 3,
                    ref passed, ref failed);

                details.DragPivot = new Vector2(0.5f, 0.5f);
                Check("Automatic drag pivot remains at visual center after one turn",
                    Vector2.Distance(details.GetRotatedDragPivot(1), new Vector2(0.5f, 0.5f)) < 0.0001f,
                    ref passed, ref failed);
                Check("Automatic drag pivot remains at visual center after four turns",
                    Vector2.Distance(details.GetRotatedDragPivot(4), new Vector2(0.5f, 0.5f)) < 0.0001f,
                    ref passed, ref failed);

                details.DragPivot = new Vector2(0.25f, 0.75f);
                Check("Custom pivot rotates clockwise with the item",
                    Vector2.Distance(details.GetRotatedDragPivot(1), new Vector2(0.25f, 0.25f)) < 0.0001f,
                    ref passed, ref failed);
                Check("Custom pivot rotates correctly through two quarter-turns",
                    Vector2.Distance(details.GetRotatedDragPivot(2), new Vector2(0.75f, 0.25f)) < 0.0001f,
                    ref passed, ref failed);
                Check("Custom pivot rotates correctly counter-clockwise",
                    Vector2.Distance(details.GetRotatedDragPivot(-1), new Vector2(0.75f, 0.75f)) < 0.0001f,
                    ref passed, ref failed);
            }
            finally
            {
                DestroyImmediate(details);
            }
            Debug.Log($"INVENTORY TOOLTIP SHAPE TESTS: {passed}/{passed + failed} passed; {failed} failed.", this);
        }

        private void Check(string description, bool condition, ref int passed, ref int failed)
        {
            if (condition) { passed++; Debug.Log("PASS: " + description, this); }
            else { failed++; Debug.LogError("FAIL: " + description, this); }
        }
    }
}
