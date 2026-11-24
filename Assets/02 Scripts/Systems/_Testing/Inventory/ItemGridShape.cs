using UnityEngine;

namespace Systems.Inventory
{
    /// <summary>Shared grid-mask helpers. Coordinates are top-left based.</summary>
    public static class ItemGridShape
    {
        public static int NormalizeRotation(int quarterTurns) => ((quarterTurns % 4) + 4) % 4;

        public static void GetRotatedSize(ItemDetails details, int quarterTurns, out int width, out int height)
        {
            width = details != null ? Mathf.Max(1, details.GridWidth) : 1;
            height = details != null ? Mathf.Max(1, details.GridHeight) : 1;
            if (NormalizeRotation(quarterTurns) % 2 != 0)
            {
                int swap = width; width = height; height = swap;
            }
        }

        public static bool IsOccupied(ItemDetails details, int x, int y, int quarterTurns)
        {
            if (details == null) return x == 0 && y == 0;
            int q = NormalizeRotation(quarterTurns);
            int width = Mathf.Max(1, details.GridWidth);
            int height = Mathf.Max(1, details.GridHeight);
            GetRotatedSize(details, q, out int rotatedWidth, out int rotatedHeight);
            if (x < 0 || y < 0 || x >= rotatedWidth || y >= rotatedHeight) return false;
            if (!details.UseCustomShape) return true;

            int sourceX, sourceY;
            switch (q)
            {
                case 1: sourceX = y; sourceY = height - 1 - x; break;
                case 2: sourceX = width - 1 - x; sourceY = height - 1 - y; break;
                case 3: sourceX = width - 1 - y; sourceY = x; break;
                default: sourceX = x; sourceY = y; break;
            }
            int index = sourceY * width + sourceX;
            return details.ShapeCells != null && index >= 0 && index < details.ShapeCells.Count && details.ShapeCells[index];
        }

        public static bool HasValidShape(ItemDetails details)
        {
            if (details == null) return true;
            if (!details.UseCustomShape) return true;
            if (details.GridWidth < 1 || details.GridHeight < 1 || details.ShapeCells == null ||
                details.ShapeCells.Count != details.GridWidth * details.GridHeight) return false;
            for (int i = 0; i < details.ShapeCells.Count; i++) if (details.ShapeCells[i]) return true;
            return false;
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
    }
}
