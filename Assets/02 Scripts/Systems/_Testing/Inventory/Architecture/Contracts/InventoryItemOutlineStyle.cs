using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Reusable hover-outline color for one or more item definitions.</summary>
    [CreateAssetMenu(fileName = "Item Outline Style", menuName = "Inventory/Item Outline Style")]
    public sealed class InventoryItemOutlineStyle : ScriptableObject
    {
        [SerializeField] private Color outlineColor = new Color(0.92f, 0.82f, 0.42f, 1f);
        public Color OutlineColor => outlineColor;
    }
}
