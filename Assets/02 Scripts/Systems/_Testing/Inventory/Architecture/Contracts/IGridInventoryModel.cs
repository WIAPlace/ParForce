namespace Systems.Inventory.Architecture
{
    /// <summary>Placement information for one item in a rectangular grid.</summary>
    public readonly struct GridItemPlacement
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int RotationQuarterTurns { get; }
        public int RotationDegrees => RotationQuarterTurns * 90;
        public bool IsRotated => RotationQuarterTurns % 2 != 0;

        public GridItemPlacement(int x, int y, int width, int height, bool isRotated)
            : this(x, y, width, height, isRotated ? 1 : 0) { }

        public GridItemPlacement(int x, int y, int width, int height, int rotationQuarterTurns)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            RotationQuarterTurns = ((rotationQuarterTurns % 4) + 4) % 4;
        }
    }

    /// <summary>
    /// Optional capability for inventories whose items occupy rectangular
    /// footprints across multiple cells.
    /// </summary>
    public interface IGridInventoryModel : IInventoryModel
    {
        int Width { get; }
        int Height { get; }
        bool TryGetItemAtCell(int x, int y, out Item item);
        bool TryGetPlacement(SerializableGuid itemInstanceId, out GridItemPlacement placement);
        bool CanPlaceAt(Item item, int x, int y, bool rotated, out InventoryOperationFailure failure);
        bool CanMoveTo(SerializableGuid itemInstanceId, int x, int y, bool rotated, out InventoryOperationFailure failure);
        bool TryAddAt(Item item, int x, int y, bool rotated, out InventoryOperationFailure failure);
        bool TryMoveTo(SerializableGuid itemInstanceId, int x, int y, bool rotated, out InventoryOperationFailure failure);
        bool CanPlaceAt(Item item, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure);
        bool CanMoveTo(SerializableGuid itemInstanceId, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure);
        bool TryAddAt(Item item, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure);
        bool TryMoveTo(SerializableGuid itemInstanceId, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure);
    }
}
