using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Rectangular grid storage. Items keep a single runtime identity while
    /// their rectangular footprint reserves multiple cells.
    /// </summary>
    public sealed class GridInventoryModel : IGridInventoryModel
    {
        private readonly List<Item> items = new List<Item>();
        private readonly ReadOnlyCollection<Item> readOnlyItems;
        private readonly Dictionary<SerializableGuid, GridItemPlacement> placements =
            new Dictionary<SerializableGuid, GridItemPlacement>();

        public SerializableGuid Id { get; }
        public string DisplayName { get; }
        public int Width { get; }
        public int Height { get; }
        public int Capacity => Width * Height;
        public int ItemCount => items.Count;
        public IReadOnlyList<Item> Items => readOnlyItems;
        public event Action<InventoryChangedEvent> Changed;

        public GridInventoryModel(SerializableGuid id, string displayName, int width, int height,
            IEnumerable<Item> startingItems = null)
        {
            if (id == SerializableGuid.Empty)
                throw new ArgumentException("An inventory must have a non-empty ID.", nameof(id));
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Grid Inventory" : displayName;
            Width = width;
            Height = height;
            readOnlyItems = items.AsReadOnly();

            if (startingItems == null) return;
            foreach (Item item in startingItems)
            {
                if (item == null || item.Id == SerializableGuid.Empty || Contains(item.Id)) continue;
                if (TryFindFirstFit(item, out int x, out int y))
                    AddWithoutNotification(item, x, y, false);
            }
        }

        public bool TryGetItem(SerializableGuid id, out Item item)
        {
            item = items.FirstOrDefault(candidate => candidate.Id == id);
            return item != null;
        }

        public bool TryGetItemAtCell(int x, int y, out Item item)
        {
            item = null;
            if (!IsInside(x, y)) return false;
            foreach (Item candidate in items)
            {
                GridItemPlacement p = placements[candidate.Id];
                if (x >= p.X && x < p.X + p.Width && y >= p.Y && y < p.Y + p.Height)
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
            int maxStack = item.details != null && item.details.category != ItemCategory.Equipment ? Math.Max(1, item.details.maxStack) : 1;
            if (item.quantity < 1 || item.quantity > maxStack) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
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
            int maximum = item.details != null && item.details.category != ItemCategory.Equipment ? Math.Max(1, item.details.maxStack) : 1;
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
            int maxStack = item.details != null && item.details.category != ItemCategory.Equipment ? Math.Max(1, item.details.maxStack) : 1;
            if (item.quantity < 1 || item.quantity > maxStack) { failure = InventoryOperationFailure.InvalidQuantity; return false; }

            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || x < 0 || y < 0 ||
                x + itemWidth > Width || y + itemHeight > Height)
            {
                failure = InventoryOperationFailure.OutOfBounds;
                return false;
            }

            if (!IsAreaClear(x, y, itemWidth, itemHeight, SerializableGuid.Empty))
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
            GetFootprint(item, rotationQuarterTurns, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || x < 0 || y < 0 ||
                x + itemWidth > Width || y + itemHeight > Height)
            {
                failure = InventoryOperationFailure.OutOfBounds;
                return false;
            }
            if (!IsAreaClear(x, y, itemWidth, itemHeight, id))
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
            GetFootprint(item, 0, out int itemWidth, out int itemHeight);
            if (itemWidth < 1 || itemHeight < 1 || itemWidth > Width || itemHeight > Height)
                return false;

            for (int y = 0; y <= Height - itemHeight; y++)
            for (int x = 0; x <= Width - itemWidth; x++)
            {
                if (!IsAreaClear(x, y, itemWidth, itemHeight, SerializableGuid.Empty)) continue;
                foundX = x;
                foundY = y;
                return true;
            }
            return false;
        }

        private bool IsAreaClear(int x, int y, int width, int height, SerializableGuid ignoreId)
        {
            foreach (KeyValuePair<SerializableGuid, GridItemPlacement> entry in placements)
            {
                if (entry.Key == ignoreId) continue;
                GridItemPlacement p = entry.Value;
                bool overlaps = x < p.X + p.Width && x + width > p.X &&
                                y < p.Y + p.Height && y + height > p.Y;
                if (overlaps) return false;
            }
            return true;
        }

        private void AddWithoutNotification(Item item, int x, int y, bool rotated)
        {
            GetFootprint(item, rotated ? 1 : 0, out int itemWidth, out int itemHeight);
            placements.Add(item.Id, new GridItemPlacement(x, y, itemWidth, itemHeight, rotated));
            items.Add(item);
        }

        private static void GetFootprint(Item item, int rotationQuarterTurns, out int width, out int height)
        {
            width = item != null && item.details != null ? Math.Max(1, item.details.GridWidth) : 1;
            height = item != null && item.details != null ? Math.Max(1, item.details.GridHeight) : 1;
            if (NormalizeRotation(rotationQuarterTurns) % 2 != 0) { int swap = width; width = height; height = swap; }
        }

        private static int NormalizeRotation(int rotationQuarterTurns) => ((rotationQuarterTurns % 4) + 4) % 4;

        private bool Contains(SerializableGuid id) => placements.ContainsKey(id);
        private bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        private void RaiseChanged(InventoryChangeKind kind, SerializableGuid id) =>
            Changed?.Invoke(new InventoryChangedEvent(Id, kind, id));
    }
}
