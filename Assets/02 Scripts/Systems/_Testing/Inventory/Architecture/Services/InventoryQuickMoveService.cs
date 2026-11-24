using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>Moves as much of one stack as possible, merging first and then creating new stacks.</summary>
    public sealed class InventoryQuickMoveService
    {
        public InventoryQuickMoveResult QuickMove(IInventoryModel source, SerializableGuid itemId,
            IEnumerable<IInventoryModel> destinations)
        {
            if (source == null || destinations == null || !source.TryGetItem(itemId, out Item sourceItem) || sourceItem == null)
                return new InventoryQuickMoveResult(0, 0, "The source item is unavailable.");

            List<IInventoryModel> orderedDestinations = destinations
                .Where(destination => destination != null && destination.Id != source.Id).ToList();
            int initialQuantity = sourceItem.quantity;
            int moved = 0;

            // Pass one: fill every compatible partial stack before considering any empty slot.
            foreach (IInventoryModel destination in orderedDestinations)
            {
                if (!source.TryGetItem(itemId, out sourceItem)) break;
                if (!CanStack(sourceItem)) continue;

                Item[] existingStacks = destination.Items.Where(candidate => candidate != null &&
                    GetDefinitionId(candidate) == GetDefinitionId(sourceItem) && CanStack(candidate) &&
                    candidate.quantity < candidate.details.maxStack).ToArray();
                foreach (Item target in existingStacks)
                {
                    if (!source.TryGetItem(itemId, out sourceItem)) break;
                    int room = target.details.maxStack - target.quantity;
                    int amount = Math.Min(room, sourceItem.quantity);
                    if (amount <= 0) continue;
                    int oldTargetQuantity = target.quantity;
                    if (!destination.TrySetQuantity(target.Id, oldTargetQuantity + amount, out _)) continue;
                    int remaining = sourceItem.quantity - amount;
                    bool sourceUpdated = remaining == 0
                        ? source.TryRemove(itemId, out _, out _)
                        : source.TrySetQuantity(itemId, remaining, out _);
                    if (!sourceUpdated)
                    {
                        destination.TrySetQuantity(target.Id, oldTargetQuantity, out _);
                        continue;
                    }
                    moved += amount;
                }
            }

            // Pass two: place the remaining quantity into empty slots, respecting destination priority.
            foreach (IInventoryModel destination in orderedDestinations)
            {
                if (!source.TryGetItem(itemId, out sourceItem)) break;
                int maxStack = GetMaxStack(sourceItem);
                int stackAmount = Math.Min(sourceItem.quantity, maxStack);
                if (stackAmount < 1) continue;

                Item addition;
                bool movingOriginal = stackAmount == sourceItem.quantity;
                GridItemPlacement originalPlacement = default;
                bool hadGridPlacement = source is IGridInventoryModel sourceGrid &&
                    sourceGrid.TryGetPlacement(itemId, out originalPlacement);
                int originalSlot = -1;
                bool hadSlot = source is ISlotInventoryModel sourceSlots && sourceSlots.TryGetSlotOf(itemId, out originalSlot);
                string originalEquipmentSlot = null;
                bool hadEquipmentSlot = source is IEquipmentInventoryModel sourceEquipment &&
                    sourceEquipment.TryGetSlotOf(itemId, out originalEquipmentSlot);

                if (movingOriginal)
                {
                    if (!destination.CanAccept(sourceItem, out _)) continue;
                    if (!source.TryRemove(itemId, out addition, out _)) continue;
                    if (!destination.TryAdd(addition, out _))
                    {
                        if (!RestoreSource(source, addition, hadGridPlacement, originalPlacement,
                            hadSlot, originalSlot, hadEquipmentSlot, originalEquipmentSlot))
                            return new InventoryQuickMoveResult(moved, initialQuantity - moved,
                                "CRITICAL: A destination rejected an item and source restoration failed.");
                        continue;
                    }
                }
                else
                {
                    addition = new Item(sourceItem.details, stackAmount);
                    if (!destination.CanAccept(addition, out _) || !destination.TryAdd(addition, out _))
                        continue;
                    if (!source.TrySetQuantity(itemId, sourceItem.quantity - stackAmount, out _))
                    {
                        destination.TryRemove(addition.Id, out _, out _);
                        continue;
                    }
                }
                moved += stackAmount;
            }

            int remainingQuantity = source.TryGetItem(itemId, out sourceItem) ? sourceItem.quantity : 0;
            return new InventoryQuickMoveResult(moved, remainingQuantity,
                moved > 0 ? "Quick-moved " + moved + " item(s)." : "No available destination could accept this item.");
        }

        /// <summary>Merges a dragged stack into the stack directly under the pointer; never creates another slot.</summary>
        public InventoryQuickMoveResult TryMergeInto(IInventoryModel source, IInventoryModel destination,
            SerializableGuid sourceItemId, SerializableGuid targetItemId)
        {
            if (source == null || destination == null || source.Id == destination.Id && sourceItemId == targetItemId ||
                !source.TryGetItem(sourceItemId, out Item sourceItem) ||
                !destination.TryGetItem(targetItemId, out Item targetItem) ||
                !CanMerge(sourceItem, targetItem))
                return new InventoryQuickMoveResult(0, 0, "These items cannot be stacked together.");

            int originalTargetQuantity = targetItem.quantity;
            int room = targetItem.details.maxStack - originalTargetQuantity;
            int amount = Math.Min(Math.Max(0, room), sourceItem.quantity);
            if (amount <= 0) return new InventoryQuickMoveResult(0, sourceItem.quantity, "The target stack is already full.");
            if (!destination.TrySetQuantity(targetItemId, originalTargetQuantity + amount, out _))
                return new InventoryQuickMoveResult(0, sourceItem.quantity, "The target stack rejected the quantity change.");

            int remaining = sourceItem.quantity - amount;
            bool sourceUpdated = remaining == 0
                ? source.TryRemove(sourceItemId, out _, out _)
                : source.TrySetQuantity(sourceItemId, remaining, out _);
            if (!sourceUpdated)
            {
                destination.TrySetQuantity(targetItemId, originalTargetQuantity, out _);
                return new InventoryQuickMoveResult(0, sourceItem.quantity, "The source stack could not be updated; the merge was rolled back.");
            }
            return new InventoryQuickMoveResult(amount, remaining, "Merged " + amount + " item(s) into the target stack.");
        }

        /// <summary>Returns true when the held item can add at least one unit to the target stack.</summary>
        public bool CanMerge(Item sourceItem, Item targetItem)
        {
            return CanStack(sourceItem) && CanStack(targetItem) &&
                GetDefinitionId(sourceItem) == GetDefinitionId(targetItem) &&
                targetItem.quantity < Math.Max(1, targetItem.details.maxStack);
        }

        private static SerializableGuid GetDefinitionId(Item item)
        {
            if (item == null) return SerializableGuid.Empty;
            // The ItemDetails reference is authoritative at runtime. Older/serialized
            // instances can have an empty or stale detailsId, so fall back to the asset ID.
            if (item.details != null && item.details.Id != SerializableGuid.Empty)
                return item.details.Id;
            return item.detailsId;
        }

        private static bool CanStack(Item item) => item != null && item.details != null && item.details.maxStack > 1;

        private static int GetMaxStack(Item item) => InventoryItemMatching.GetMaxStack(item);

        private static bool RestoreSource(IInventoryModel source, Item item, bool hadGridPlacement,
            GridItemPlacement placement, bool hadSlot, int slot, bool hadEquipmentSlot, string equipmentSlot)
        {
            if (hadGridPlacement && source is IGridInventoryModel grid)
                return grid.TryAddAt(item, placement.X, placement.Y, placement.RotationQuarterTurns, out _);
            if (hadEquipmentSlot && source is IEquipmentInventoryModel equipment)
                return equipment.TryAddAt(item, equipmentSlot, out _);
            if (hadSlot && source is ISlotInventoryModel slots)
                return slots.TryAddAt(item, slot, out _);
            return source.TryAdd(item, out _);
        }
    }

    public sealed class InventoryQuickMoveResult
    {
        public int MovedQuantity { get; }
        public int RemainingQuantity { get; }
        public string Message { get; }
        public bool Success => MovedQuantity > 0;
        public InventoryQuickMoveResult(int movedQuantity, int remainingQuantity, string message)
        { MovedQuantity = movedQuantity; RemainingQuantity = remainingQuantity; Message = message; }
    }
}
