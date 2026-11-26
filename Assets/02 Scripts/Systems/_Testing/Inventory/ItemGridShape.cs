using UnityEngine;

namespace Systems.Inventory
{
    /// <summary>Shared grid-mask helpers. Public footprint coordinates are normalized to the
    /// smallest rectangle containing occupied cells, so empty padding in a shape asset never
    /// consumes inventory space.</summary>
    public static class ItemGridShape
    {
        public static int NormalizeRotation(int quarterTurns) => ((quarterTurns % 4) + 4) % 4;

        public static void GetUnrotatedBounds(ItemDetails details, out int minX, out int minY, out int width, out int height)
        {
            minX = 0; minY = 0;
            if (details == null) { width = 1; height = 1; return; }
            int sourceWidth = Mathf.Max(1, details.GridWidth);
            int sourceHeight = Mathf.Max(1, details.GridHeight);
            if (!details.UseCustomShape) { width = sourceWidth; height = sourceHeight; return; }

            int maxX = -1, maxY = -1;
            minX = sourceWidth; minY = sourceHeight;
            if (details.ShapeCells != null)
            {
                int count = Mathf.Min(details.ShapeCells.Count, sourceWidth * sourceHeight);
                for (int i = 0; i < count; i++)
                {
                    if (!details.ShapeCells[i]) continue;
                    int x = i % sourceWidth;
                    int y = i / sourceWidth;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                }
            }
            if (maxX < minX || maxY < minY) { minX = minY = 0; width = height = 0; return; }
            width = maxX - minX + 1;
            height = maxY - minY + 1;
        }

        public static void GetRotatedSize(ItemDetails details, int quarterTurns, out int width, out int height)
        {
            GetUnrotatedBounds(details, out _, out _, out width, out height);
            if (width < 1 || height < 1) { width = height = 1; return; }
            if (NormalizeRotation(quarterTurns) % 2 != 0) { int swap = width; width = height; height = swap; }
        }

        public static bool IsOccupied(ItemDetails details, int x, int y, int quarterTurns)
        {
            if (details == null) return x == 0 && y == 0;
            int q = NormalizeRotation(quarterTurns);
            GetUnrotatedBounds(details, out int minX, out int minY, out int width, out int height);
            if (width < 1 || height < 1) return false;
            int rotatedWidth = q % 2 == 0 ? width : height;
            int rotatedHeight = q % 2 == 0 ? height : width;
            if (x < 0 || y < 0 || x >= rotatedWidth || y >= rotatedHeight) return false;
            if (!details.UseCustomShape) return true;

            int localX, localY;
            switch (q)
            {
                case 1: localX = y; localY = height - 1 - x; break;
                case 2: localX = width - 1 - x; localY = height - 1 - y; break;
                case 3: localX = width - 1 - y; localY = x; break;
                default: localX = x; localY = y; break;
            }
            int sourceX = minX + localX;
            int sourceY = minY + localY;
            int index = sourceY * Mathf.Max(1, details.GridWidth) + sourceX;
            return details.ShapeCells != null && index >= 0 && index < details.ShapeCells.Count && details.ShapeCells[index];
        }

        public static bool HasValidShape(ItemDetails details)
        {
            if (details == null || !details.UseCustomShape) return true;
            if (details.GridWidth < 1 || details.GridHeight < 1 || details.ShapeCells == null ||
                details.ShapeCells.Count != details.GridWidth * details.GridHeight) return false;
            return OccupiedCellCount(details) > 0;
        }

        public static int OccupiedCellCount(ItemDetails details)
        {
            if (details == null) return 1;
            if (!details.UseCustomShape) return Mathf.Max(1, details.GridWidth) * Mathf.Max(1, details.GridHeight);
            if (details.ShapeCells == null) return 0;
            int count = 0;
            foreach (bool cell in details.ShapeCells) if (cell) count++;
            return count;
        }

        /// <summary>Returns the uniform icon size in grid-cell units. A zero setting
        /// automatically uses the longest dimension of the normalized occupied footprint.</summary>
        public static float GetIconSizeInCells(ItemDetails details)
        {
            GetRotatedSize(details, 0, out int width, out int height);
            if (details == null) return Mathf.Max(width, height);
            return details.IconSizeInCells > 0f ? details.IconSizeInCells : Mathf.Max(width, height);
        }
    }
}
