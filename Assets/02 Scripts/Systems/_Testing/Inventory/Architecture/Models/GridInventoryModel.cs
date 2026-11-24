using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Grid storage with rectangular bounds and an optional usable-cell mask.
    /// Items keep a single runtime identity while their occupied footprint reserves cells.
    /// </summary>
    public sealed class GridInventoryModel : IGridInventoryModel
    {
        private readonly List<Item> items = new List<Item>();
        private readonly ReadOnlyCollection<Item> readOnlyItems;
        private readonly Dictionary<SerializableGuid, GridItemPlacement> placements =
            new Dictionary<SerializableGuid, GridItemPlacement>();
        private readonly bool[] usableCells;

        public SerializableGuid Id { get; }
        public string DisplayName { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>
        /// Number of usable cells permitted by the layout mask. This is intentionally
        /// not the rectangular coordinate area (Width * Height) when cells are blocked.
        /// Kept as the common IInventoryModel capacity value for compatibility.
        /// </summary>
        public int Capacity { get; }
        /// <summary>Total number of usable cells, excluding cells blocked by the layout mask.</summary>
        public int TotalUsableCells => Capacity;
        /// <summary>Number of usable cells currently occupied by item shape cells.</summary>
        public int OccupiedCells
        {
            get
            {
                int occupied = 0;
                foreach (Item item in items)
                {
                    if (item == null || !placements.TryGetValue(item.Id, out GridItemPlacement placement))
                        continue;

                    for (int y = 0; y < placement.Height; y++)
                    for (int x = 0; x < placement.Width; x++)
                        if (ItemGridShape.IsOccupied(item.details, x, y, placement.RotationQuarterTurns))
                            occupied++;
                }
                return occupied;
            }
        }
        /// <summary>Number of usable cells not currently occupied by item shape cells.</summary>
        public int AvailableCells => Math.Max(0, TotalUsableCells - OccupiedCells);
        /// <summary>Percentage of usable cells occupied, from 0 to 100. Empty grids report 0.</summary>
        public float FillPercentage => TotalUsableCells <= 0
            ? 0f
            : (float)OccupiedCells * 100f / TotalUsableCells;
        public int ItemCount => items.Count;
        public IReadOnlyList<Item> Items => readOnlyItems;
        public event Action<InventoryChangedEvent> Changed;

        /// <summary>
        /// Creates a grid inventory directly from a designer-authored layout asset.
        /// Dimensions and usable cells are copied into this model as independent runtime
        /// configuration. The model does not retain a Unity asset dependency.
        /// </summary>
        public GridInventoryModel(SerializableGuid id, string displayName, InventoryGridLayout layout,
            IEnumerable<Item> startingItems = null)
            : this(id, displayName,
                layout != null ? layout.Width : throw new ArgumentNullException(nameof(layout)),
                layout.Height, startingItems, layout.GetUsableCellsCopy())
        {
        }

        public GridInventoryModel(SerializableGuid id, string displayName, int width, int height,
            IEnumerable<Item> startingItems = null, IReadOnlyList<bool> usableCellMask = null)
        {
            if (id == SerializableGuid.Empty)
                throw new ArgumentException("An inventory must have a non-empty ID.", nameof(id));
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Grid Inventory" : displayName;
            Width = width;
            Height = height;
            usableCells = new bool[Width * Height];
            int usableCount = 0;
            for (int i = 0; i < usableCells.Length; i++)
            {
                usableCells[i] = usableCellMask == null || i >= usableCellMask.Count || usableCellMask[i];
                if (usableCells[i]) usableCount++;
            }
            Capacity = usableCount;
            readOnlyItems = items.AsReadOnly();

            if (startingItems == null) return;
            foreach (Item item in startingItems)
            {
                if (item == null || item.Id == SerializableGuid.Empty || Contains(item.Id) ||
                    !ItemGridShape.HasValidShape(item.details)) continue;
                if (TryFindFirstFit(item, out int x, out int y))
                    AddWithoutNotification(item, x, y, false);
            }
        }

        public bool TryGetItem(SerializableGuid id, out Item item)
        {
            item = items.FirstOrDefault(candidate => candidate.Id == id);
            return item != null;
        }

        public bool IsCellUsable(int x, int y) => IsInside(x, y) && usableCells[y * Width + x];

        public bool TryGetItemAtCell(int x, int y, out Item item)
        {
            item = null;
            if (!IsCellUsable(x, y)) return false;
            foreach (Item candidate in items)
            {
                GridItemPlacement p = placements[candidate.Id];
                if (x >= p.X && x < p.X + p.Width && y >= p.Y && y < p.Y + p.Height &&
                    ItemGridShape.IsOccupied(candidate.details, x - p.X, y - p.Y, p.RotationQuarterTurns))
                {
                    item = candidate;
                    return true;
                }
            }
            return false;
        }

        public bool TryGetPlacement(SerializableGuid id, out GridItemPlacement placement) =>
            placements.TryGetValue(id, out placement);

        public bool CanAccept(Item item, out InventoryOperationFailure failure)
        {
            if (item == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (item.Id == SerializableGuid.Empty) { failure = InventoryOperationFailure.InvalidItemId; return false; }
            if (Contains(item.Id)) { failure = InventoryOperationFailure.DuplicateItem; return false; }
            int maxStack = InventoryItemMatching.GetMaxStack(item);
            if (item.quantity < 1 || item.quantity > maxStack) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!ItemGridShape.HasValidShape(item.details)) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (!TryFindFirstFit(item, out _, out _))
            {
                failure = InventoryOperationFailure.InventoryFull;
                return false;
            }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryAdd(Item item, out InventoryOperationFailure failure)
        {
            if (!CanAccept(item, out failure)) return false;
            TryFindFirstFit(item, out int x, out int y);
            return TryAddAt(item, x, y, false, out failure);
        }

        public bool TrySetQuantity(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
        {
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            int maximum = InventoryItemMatching.GetMaxStack(item);
            if (quantity < 1 || quantity > maximum) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            item.quantity = quantity;
            RaiseChanged(InventoryChangeKind.StackChanged, itemInstanceId);
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool CanPlaceAt(Item item, int x, int y, bool rotated, out InventoryOperationFailure failure) =>
            CanPlaceAt(item, x, y, rotated ? 1 : 0, out failure);

        public bool CanPlaceAt(Item item, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure)
        {
            if (item == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (item.Id == SerializableGuid.Empty) { failure = InventoryOperationFailure.InvalidItemId; return false; }
            if (Contains(item.Id)) { failure = InventoryOperationFailure.DuplicateItem; return false; }
            int maxStack = InventoryItemMatching.GetMaxStack(item);
            if (item.quantity < 1 || item.quantity > maxStack) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!ItemGridShape.HasValidShape(item.details)) { failure = InventoryOperationFailure.InvalidItem; return false; }

            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || x < 0 || y < 0 ||
                x + itemWidth > Width || y + itemHeight > Height)
            {
                failure = InventoryOperationFailure.OutOfBounds;
                return false;
            }

            if (!IsShapeAreaClear(item, x, y, rotationQuarterTurns, SerializableGuid.Empty))
            {
                failure = InventoryOperationFailure.PlacementOccupied;
                return false;
            }

            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryAddAt(Item item, int x, int y, bool rotated, out InventoryOperationFailure failure) =>
            TryAddAt(item, x, y, rotated ? 1 : 0, out failure);

        public bool TryAddAt(Item item, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure)
        {
            rotationQuarterTurns = NormalizeRotation(rotationQuarterTurns);
            if (!CanPlaceAt(item, x, y, rotationQuarterTurns, out failure)) return false;
            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            placements.Add(item.Id, new GridItemPlacement(x, y, itemWidth, itemHeight, rotationQuarterTurns));
            items.Add(item);
            RaiseChanged(InventoryChangeKind.ItemAdded, item.Id);
            return true;
        }

        public bool CanMoveTo(SerializableGuid id, int x, int y, bool rotated,
            out InventoryOperationFailure failure) => CanMoveTo(id, x, y, rotated ? 1 : 0, out failure);

        public bool CanMoveTo(SerializableGuid id, int x, int y, int rotationQuarterTurns,
            out InventoryOperationFailure failure)
        {
            if (!placements.ContainsKey(id))
            {
                failure = InventoryOperationFailure.ItemNotFound;
                return false;
            }

            Item item = items.First(candidate => candidate.Id == id);
            if (!ItemGridShape.HasValidShape(item.details))
            {
                failure = InventoryOperationFailure.InvalidItem;
                return false;
            }
            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || x < 0 || y < 0 ||
                x + itemWidth > Width || y + itemHeight > Height)
            {
                failure = InventoryOperationFailure.OutOfBounds;
                return false;
            }
            if (!IsShapeAreaClear(item, x, y, rotationQuarterTurns, id))
            {
                failure = InventoryOperationFailure.PlacementOccupied;
                return false;
            }

            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryMoveTo(SerializableGuid id, int x, int y, bool rotated,
            out InventoryOperationFailure failure) => TryMoveTo(id, x, y, rotated ? 1 : 0, out failure);

        public bool TryMoveTo(SerializableGuid id, int x, int y, int rotationQuarterTurns,
            out InventoryOperationFailure failure)
        {
            rotationQuarterTurns = NormalizeRotation(rotationQuarterTurns);
            if (!CanMoveTo(id, x, y, rotationQuarterTurns, out failure))
                return false;

            Item item = items.First(candidate => candidate.Id == id);
            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            placements[id] = new GridItemPlacement(x, y, itemWidth, itemHeight, rotationQuarterTurns);
            RaiseChanged(InventoryChangeKind.ContentsReplaced, id);
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryRemoveQuantity(SerializableGuid itemInstanceId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            removedQuantity = Math.Min(quantity, item.quantity);
            int remaining = item.quantity - removedQuantity;
            if (remaining == 0)
            {
                if (!TryRemove(itemInstanceId, out _, out failure)) { removedQuantity = 0; return false; }
            }
            else if (!TrySetQuantity(itemInstanceId, remaining, out failure)) { removedQuantity = 0; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryRemoveQuantity(Item item, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            if (item == null) { removedQuantity = 0; failure = InventoryOperationFailure.InvalidItem; return false; }
            return TryRemoveQuantity(item.Id, quantity, out removedQuantity, out failure);
        }

        public bool TryRemoveQuantity(ItemDetails definition, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            Item[] matches = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).ToArray();
            if (matches.Length == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            int remaining = quantity;
            foreach (Item item in matches)
            {
                if (remaining <= 0) break;
                if (!TryRemoveQuantity(item.Id, remaining, out int taken, out failure)) continue;
                removedQuantity += taken;
                remaining -= taken;
            }
            failure = InventoryOperationFailure.None;
            return removedQuantity > 0;
        }

        public bool TryRemoveQuantityAtIndex(int itemIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (itemIndex < 0 || itemIndex >= Items.Count || Items[itemIndex] == null)
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(Items[itemIndex].Id, quantity, out removedQuantity, out failure);
        }

        public bool TryRemoveQuantityAtCell(int x, int y, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (!TryGetItemAtCell(x, y, out Item item))
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(item.Id, quantity, out removedQuantity, out failure);
        }

        public bool TryRemoveExact(ItemDetails definition, int quantity, out InventoryOperationFailure failure)
        {
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available < quantity) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryRemoveQuantity(definition, quantity, out int removed, out failure) || removed != quantity)
            { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        public bool TryRemoveAll(ItemDetails definition, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return TryRemoveQuantity(definition, available, out removedQuantity, out failure);
        }

        public bool TryRemove(SerializableGuid id, out Item removedItem,
            out InventoryOperationFailure failure)
        {
            int index = items.FindIndex(candidate => candidate.Id == id);
            if (index < 0)
            {
                removedItem = null;
                failure = InventoryOperationFailure.ItemNotFound;
                return false;
            }

            removedItem = items[index];
            items.RemoveAt(index);
            placements.Remove(id);
            failure = InventoryOperationFailure.None;
            RaiseChanged(InventoryChangeKind.ItemRemoved, id);
            return true;
        }

        private bool TryFindFirstFit(Item item, out int foundX, out int foundY)
        {
            foundX = -1;
            foundY = -1;
            if (item == null || !ItemGridShape.HasValidShape(item.details)) return false;
            GetFootprint(item, 0, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || itemWidth > Width || itemHeight > Height)
                return false;

            for (int y = 0; y <= Height - itemHeight; y++)
            for (int x = 0; x <= Width - itemWidth; x++)
            {
                if (!IsShapeAreaClear(item, x, y, 0, SerializableGuid.Empty)) continue;
                foundX = x;
                foundY = y;
                return true;
            }
            return false;
        }

        private bool IsShapeAreaClear(Item item, int x, int y, int rotationQuarterTurns, SerializableGuid ignoreId)
        {
            GetFootprint(item, rotationQuarterTurns, out int width, out int height);
            for (int localY = 0; localY < height; localY++)
            for (int localX = 0; localX < width; localX++)
            {
                if (!ItemGridShape.IsOccupied(item.details, localX, localY, rotationQuarterTurns)) continue;
                int worldX = x + localX;
                int worldY = y + localY;
                if (!IsCellUsable(worldX, worldY)) return false;
                foreach (KeyValuePair<SerializableGuid, GridItemPlacement> entry in placements)
                {
                    if (entry.Key == ignoreId) continue;
                    GridItemPlacement existing = entry.Value;
                    int existingX = worldX - existing.X;
                    int existingY = worldY - existing.Y;
                    if (existingX < 0 || existingY < 0 || existingX >= existing.Width || existingY >= existing.Height)
                        continue;
                    Item existingItem = items.FirstOrDefault(candidate => candidate.Id == entry.Key);
                    if (existingItem != null && ItemGridShape.IsOccupied(existingItem.details, existingX, existingY,
                        existing.RotationQuarterTurns)) return false;
                }
            }
            return true;
        }

        private void AddWithoutNotification(Item item, int x, int y, bool rotated)
        {
            GetFootprint(item, rotated ? 1 : 0, out int itemWidth, out int itemHeight);
            placements.Add(item.Id, new GridItemPlacement(x, y, itemWidth, itemHeight, rotated));
            items.Add(item);
        }

        private static void GetFootprint(Item item, int rotationQuarterTurns, out int width, out int height) =>
            ItemGridShape.GetRotatedSize(item != null ? item.details : null, rotationQuarterTurns, out width, out height);

        private static int NormalizeRotation(int rotationQuarterTurns) => ItemGridShape.NormalizeRotation(rotationQuarterTurns);

        private bool Contains(SerializableGuid id) => placements.ContainsKey(id);
        private bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        private void RaiseChanged(InventoryChangeKind kind, SerializableGuid id) =>
            Changed?.Invoke(new InventoryChangedEvent(Id, kind, id));
    }
}
