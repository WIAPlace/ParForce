using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Read-only item data supplied to custom tooltip sections.</summary>
    public sealed class InventoryItemTooltipContext
    {
        public Item Item { get; }
        public int RotationQuarterTurns { get; }
        public int FootprintWidth { get; }
        public int FootprintHeight { get; }
        public int OccupiedCellCount { get; }

        public InventoryItemTooltipContext(Item item, int rotationQuarterTurns)
        {
            Item = item;
            RotationQuarterTurns = ItemGridShape.NormalizeRotation(rotationQuarterTurns);
            ItemGridShape.GetRotatedSize(item != null ? item.details : null, RotationQuarterTurns,
                out int width, out int height);
            FootprintWidth = width;
            FootprintHeight = height;
            OccupiedCellCount = ItemGridShape.OccupiedCellCount(item != null ? item.details : null);
        }
    }
}
