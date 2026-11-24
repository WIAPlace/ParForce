using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Designer-authored usable-cell mask for a grid inventory.</summary>
    [CreateAssetMenu(fileName = "New Inventory Grid Layout", menuName = "Inventory/Grid Layout")]
    public sealed class InventoryGridLayout : ScriptableObject
    {
        [SerializeField, Min(1)] private int width = 8;
        [SerializeField, Min(1)] private int height = 6;
        [SerializeField] private List<bool> usableCells = new List<bool>();

        public int Width => Mathf.Max(1, width);
        public int Height => Mathf.Max(1, height);
        public int UsableCellCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Width * Height && i < usableCells.Count; i++)
                    if (usableCells[i]) count++;
                return count;
            }
        }

        public bool IsCellUsable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            int index = y * Width + x;
            return index < usableCells.Count && usableCells[index];
        }

        /// <summary>Returns a copy so runtime model state cannot mutate the authoring asset.</summary>
        public bool[] GetUsableCellsCopy()
        {
            bool[] copy = new bool[Width * Height];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = i < usableCells.Count ? usableCells[i] : true;
            return copy;
        }

        private void OnEnable() => EnsureMaskSize();

        private void OnValidate()
        {
            width = Mathf.Clamp(width, 1, 24);
            height = Mathf.Clamp(height, 1, 24);
            EnsureMaskSize();
        }

        private void EnsureMaskSize()
        {
            if (usableCells == null) usableCells = new List<bool>();
            int required = Mathf.Max(1, width) * Mathf.Max(1, height);
            while (usableCells.Count < required) usableCells.Add(true);
            while (usableCells.Count > required) usableCells.RemoveAt(usableCells.Count - 1);
        }
    }
}
