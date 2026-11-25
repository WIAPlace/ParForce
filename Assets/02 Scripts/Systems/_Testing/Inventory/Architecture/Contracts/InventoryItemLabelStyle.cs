using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Reusable color styling for an item's displayed name and quantity label.</summary>
    [CreateAssetMenu(fileName = "Item Label Style", menuName = "Inventory/Item Label Style")]
    public sealed class InventoryItemLabelStyle : ScriptableObject
    {
        [SerializeField] private Color labelColor = Color.white;
        public Color LabelColor => labelColor;
    }
}
