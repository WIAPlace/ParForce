using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Reusable visual theme for an inventory panel. A model never depends on this asset;
    /// the panel view applies its USS and optional sprite references.
    /// </summary>
    [CreateAssetMenu(fileName = "New Inventory Appearance", menuName = "Inventory/Presentation/Inventory Appearance")]
    public sealed class InventoryAppearance : ScriptableObject
    {
        [Header("Styles")]
        [SerializeField] private StyleSheet styleSheet;

        [Header("Panel Visuals")]
        [SerializeField] private Sprite panelBackground;
        [SerializeField] private Sprite headerBackground;

        [Header("Slot Visuals")]
        [SerializeField] private Sprite slotBackground;
        [SerializeField] private Sprite blockedCellBackground;
        [SerializeField] private Sprite occupiedItemCellBackground;

        public StyleSheet StyleSheet => styleSheet;
        public Sprite PanelBackground => panelBackground;
        public Sprite HeaderBackground => headerBackground;
        public Sprite SlotBackground => slotBackground;
        public Sprite BlockedCellBackground => blockedCellBackground;
        public Sprite OccupiedItemCellBackground => occupiedItemCellBackground;
    }
}
