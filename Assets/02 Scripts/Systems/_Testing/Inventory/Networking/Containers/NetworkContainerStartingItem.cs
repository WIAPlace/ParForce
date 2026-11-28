using System;
using Systems.Inventory;
using UnityEngine;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>One designer-authored item stack used to initialize a shared container on the server.</summary>
    [Serializable]
    public sealed class NetworkContainerStartingItem
    {
        [SerializeField] private ItemDetails definition;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, Tooltip("When disabled, the server places this item in the first available valid location.")]
        private bool useExplicitPlacement;
        [SerializeField, Min(0)] private int x;
        [SerializeField, Min(0)] private int y;
        [SerializeField, Range(0, 3)] private int rotationQuarterTurns;

        public ItemDetails Definition => definition;
        public int Quantity => Mathf.Max(1, quantity);
        public bool UseExplicitPlacement => useExplicitPlacement;
        public int X => Mathf.Max(0, x);
        public int Y => Mathf.Max(0, y);
        public int RotationQuarterTurns => ((rotationQuarterTurns % 4) + 4) % 4;
    }
}
