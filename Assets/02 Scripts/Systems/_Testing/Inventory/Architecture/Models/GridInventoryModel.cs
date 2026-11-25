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
        public event Action<InventoryGameplayEvent> GameplayEventRaised;

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

        private bool AddCore(Item item, out InventoryOperationFailure failure)
        {
            if (!CanAccept(item, out failure)) return false;
            TryFindFirstFit(item, out int x, out int y);
            return AddAtCore(item, x, y, false, out failure);
        }

        private bool SetQuantityCore(SerializableGuid itemInstanceId, int quantity, out InventoryOperationFailure failure)
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

        private bool AddAtCore(Item item, int x, int y, bool rotated, out InventoryOperationFailure failure) =>
            AddAtCore(item, x, y, rotated ? 1 : 0, out failure);

        private bool AddAtCore(Item item, int x, int y, int rotationQuarterTurns, out InventoryOperationFailure failure)
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

        private bool MoveToCore(SerializableGuid id, int x, int y, bool rotated,
            out InventoryOperationFailure failure) => MoveToCore(id, x, y, rotated ? 1 : 0, out failure);

        private bool MoveToCore(SerializableGuid id, int x, int y, int rotationQuarterTurns,
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

        private bool RemoveQuantityCore(SerializableGuid itemInstanceId, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            if (!TryGetItem(itemInstanceId, out Item item)) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            removedQuantity = Math.Min(quantity, item.quantity);
            int remaining = item.quantity - removedQuantity;
            if (remaining == 0)
            {
                if (!RemoveCore(itemInstanceId, out _, out failure)) { removedQuantity = 0; return false; }
            }
            else if (!SetQuantityCore(itemInstanceId, remaining, out failure)) { removedQuantity = 0; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool RemoveQuantityCore(Item item, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            if (item == null) { removedQuantity = 0; failure = InventoryOperationFailure.InvalidItem; return false; }
            return RemoveQuantityCore(item.Id, quantity, out removedQuantity, out failure);
        }

        private bool RemoveQuantityCore(ItemDetails definition, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
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
                if (!RemoveQuantityCore(item.Id, remaining, out int taken, out failure)) continue;
                removedQuantity += taken;
                remaining -= taken;
            }
            failure = InventoryOperationFailure.None;
            return removedQuantity > 0;
        }

        private bool RemoveQuantityAtIndexCore(int itemIndex, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (itemIndex < 0 || itemIndex >= Items.Count || Items[itemIndex] == null)
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(Items[itemIndex].Id, quantity, out removedQuantity, out failure);
        }

        private bool RemoveQuantityAtCellCore(int x, int y, int quantity, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (!TryGetItemAtCell(x, y, out Item item))
            { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(item.Id, quantity, out removedQuantity, out failure);
        }

        private bool RemoveQuantityExactCore(ItemDetails definition, int quantity, out InventoryOperationFailure failure)
        {
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            if (quantity <= 0) { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            Item[] matches = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).ToArray();
            if (matches.Any(item => item.quantity < 1 || item.quantity > InventoryItemMatching.GetMaxStack(item)))
            { failure = InventoryOperationFailure.InvalidQuantity; return false; }
            int available = matches.Sum(item => item.quantity);
            if (available < quantity) { failure = InventoryOperationFailure.InsufficientQuantity; return false; }
            if (!RemoveQuantityCore(definition, quantity, out int removed, out failure)) return false;
            if (removed != quantity) { failure = InventoryOperationFailure.InsufficientQuantity; return false; }
            failure = InventoryOperationFailure.None;
            return true;
        }

        private bool RemoveAllCore(ItemDetails definition, out int removedQuantity, out InventoryOperationFailure failure)
        {
            removedQuantity = 0;
            if (definition == null) { failure = InventoryOperationFailure.InvalidItem; return false; }
            int available = Items.Where(item => InventoryItemMatching.MatchesDefinition(item, definition)).Sum(item => Math.Max(0, item.quantity));
            if (available == 0) { failure = InventoryOperationFailure.ItemNotFound; return false; }
            return RemoveQuantityCore(definition, available, out removedQuantity, out failure);
        }

        private bool RemoveCore(SerializableGuid id, out Item removedItem,
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


        public InventoryOperationResult Add(Item item)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = AddCore(item, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Add,
                item != null ? item.quantity : 0, success, failure, before,
                focusItemId: item != null ? item.Id : SerializableGuid.Empty, addedItem: success ? item : null));
        }

        public InventoryOperationResult SetQuantity(SerializableGuid itemInstanceId, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = SetQuantityCore(itemInstanceId, quantity, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.SetQuantity,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult Remove(SerializableGuid itemInstanceId)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveCore(itemInstanceId, out Item removedItem, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.Remove,
                removedItem != null ? removedItem.quantity : 0, success, failure, before,
                removedItem, itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(SerializableGuid itemInstanceId, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(itemInstanceId, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(Item item, int quantity)
        {
            if (item == null)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityUpTo,
                    quantity, InventoryOperationFailure.InvalidItem);
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(item, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: item.Id));
        }

        public InventoryOperationResult RemoveQuantityAtIndex(int itemIndex, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            SerializableGuid focusId = itemIndex >= 0 && itemIndex < Items.Count && Items[itemIndex] != null
                ? Items[itemIndex].Id : SerializableGuid.Empty;
            bool success = RemoveQuantityAtIndexCore(itemIndex, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: focusId));
        }

        public InventoryOperationResult RemoveQuantityExact(SerializableGuid itemInstanceId, int quantity)
        {
            if (quantity <= 0)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.InvalidQuantity);
            if (!TryGetItem(itemInstanceId, out Item item))
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.ItemNotFound);
            if (item.quantity < quantity)
                return InventoryOperationResult.Failed(this, InventoryOperationKind.RemoveQuantityExact,
                    quantity, InventoryOperationFailure.InsufficientQuantity);

            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(itemInstanceId, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityExact,
                quantity, success, failure, before, focusItemId: itemInstanceId));
        }

        public InventoryOperationResult RemoveQuantityUpTo(ItemDetails definition, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityCore(definition, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before));
        }

        public InventoryOperationResult RemoveQuantityExact(ItemDetails definition, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveQuantityExactCore(definition, quantity, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityExact,
                quantity, success, failure, before));
        }

        public InventoryOperationResult RemoveAll(ItemDetails definition)
        {
            int requested = definition == null ? 0 : Items
                .Where(item => InventoryItemMatching.MatchesDefinition(item, definition))
                .Sum(item => Math.Max(0, item.quantity));
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = RemoveAllCore(definition, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveAll,
                requested, success, failure, before));
        }

        public InventoryOperationResult AddAt(Item item, int x, int y, bool rotated) =>
            AddAt(item, x, y, rotated ? 1 : 0);

        public InventoryOperationResult AddAt(Item item, int x, int y, int rotationQuarterTurns)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            bool success = AddAtCore(item, x, y, rotationQuarterTurns, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.AddAt,
                item != null ? item.quantity : 0, success, failure, before,
                focusItemId: item != null ? item.Id : SerializableGuid.Empty, addedItem: success ? item : null));
        }

        public InventoryOperationResult MoveTo(SerializableGuid itemInstanceId, int x, int y, bool rotated) =>
            MoveTo(itemInstanceId, x, y, rotated ? 1 : 0);

        public InventoryOperationResult MoveTo(SerializableGuid itemInstanceId, int x, int y, int rotationQuarterTurns)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            int normalizedRotation = ((rotationQuarterTurns % 4) + 4) % 4;
            bool changesPosition = TryGetPlacement(itemInstanceId, out GridItemPlacement currentPlacement) &&
                (currentPlacement.X != x || currentPlacement.Y != y ||
                 currentPlacement.RotationQuarterTurns != normalizedRotation);
            bool success = MoveToCore(itemInstanceId, x, y, rotationQuarterTurns, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.MoveTo,
                0, success, failure, before, focusItemId: changesPosition ? itemInstanceId : SerializableGuid.Empty));
        }

        public InventoryOperationResult RemoveQuantityAtCell(int x, int y, int quantity)
        {
            InventoryOperationSnapshot before = InventoryOperationResult.Capture(this);
            SerializableGuid focusId = TryGetItemAtCell(x, y, out Item item) ? item.Id : SerializableGuid.Empty;
            bool success = RemoveQuantityAtCellCore(x, y, quantity, out _, out InventoryOperationFailure failure);
            return PublishGameplayEvents(InventoryOperationResult.Complete(this, InventoryOperationKind.RemoveQuantityUpTo,
                quantity, success, failure, before, focusItemId: focusId));
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
        private InventoryOperationResult PublishGameplayEvents(InventoryOperationResult result)
        {
            InventoryGameplayEventPublisher.Publish(result, gameplayEvent => GameplayEventRaised?.Invoke(gameplayEvent));
            return result;
        }

    }
}
