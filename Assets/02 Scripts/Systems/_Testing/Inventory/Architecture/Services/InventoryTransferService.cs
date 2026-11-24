namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Coordinates item movement between inventory models. This class has no
    /// Unity/UI dependency and can be reused by drag-and-drop, menus, AI, etc.
    /// </summary>
    public sealed class InventoryTransferService
    {
        /// <summary>Validates a transfer without mutating either inventory.</summary>
        public InventoryTransferResult Preview(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId)
        {
            Item item;
            InventoryTransferFailure failure;
            InventoryOperationFailure operationFailure;
            string message;

            if (!TryValidate(source, destination, itemInstanceId, out item,
                    out failure, out operationFailure, out message))
                return CreateResult(false, true, failure, operationFailure,
                    source, destination, itemInstanceId, message);

            return CreateResult(true, true, InventoryTransferFailure.None,
                InventoryOperationFailure.None, source, destination,
                itemInstanceId, "Transfer is valid.");
        }

        /// <summary>
        /// Moves the exact runtime Item instance. The destination is validated
        /// before the source is changed. If adding unexpectedly fails after
        /// removal, the service attempts to restore the item to the source.
        /// </summary>
        public InventoryTransferResult TryTransfer(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId)
        {
            Item item;
            InventoryTransferFailure failure;
            InventoryOperationFailure operationFailure;
            string message;

            if (!TryValidate(source, destination, itemInstanceId, out item,
                    out failure, out operationFailure, out message))
                return CreateResult(false, false, failure, operationFailure,
                    source, destination, itemInstanceId, message);

            IEquipmentInventoryModel sourceEquipment = source as IEquipmentInventoryModel;
            string originalEquipmentSlot = null;
            bool hadEquipmentSlot = sourceEquipment != null && sourceEquipment.TryGetSlotOf(itemInstanceId, out originalEquipmentSlot);

            Item removedItem;
            if (!source.TryRemove(itemInstanceId, out removedItem, out operationFailure))
                return CreateResult(false, false,
                    InventoryTransferFailure.SourceRemovalFailed, operationFailure,
                    source, destination, itemInstanceId,
                    "The source inventory could not remove the item.");

            if (destination.TryAdd(removedItem, out operationFailure))
                return CreateResult(true, false, InventoryTransferFailure.None,
                    InventoryOperationFailure.None, source, destination,
                    itemInstanceId, "Item transferred successfully.");

            // A custom model may reject an item after CanAccept previously
            // succeeded. Restore the exact same instance to prevent item loss.
            InventoryOperationFailure rollbackFailure;
            bool restored = hadEquipmentSlot
                ? sourceEquipment.TryAddAt(removedItem, originalEquipmentSlot, out rollbackFailure)
                : source.TryAdd(removedItem, out rollbackFailure);
            if (restored)
                return CreateResult(false, false,
                    InventoryTransferFailure.DestinationAddFailed, operationFailure,
                    source, destination, itemInstanceId,
                    "The destination rejected the item. It was restored to the source.");

            return CreateResult(false, false, InventoryTransferFailure.RollbackFailed,
                rollbackFailure, source, destination, itemInstanceId,
                "CRITICAL: The destination rejected the item and the source could not restore it. " +
                "The inventories require reconciliation.");
        }

        /// <summary>
        /// Transfers an item into an explicitly selected collection slot or grid
        /// cell. The source placement is captured so a failed destination add
        /// can restore the item to its original location.
        /// </summary>
        public InventoryTransferResult TryTransferAt(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId,
            int destinationIndex)
        {
            return TryTransferAt(source, destination, itemInstanceId, destinationIndex, false);
        }

        public InventoryTransferResult TryTransferAt(
            IInventoryModel source, IInventoryModel destination, SerializableGuid itemInstanceId,
            int destinationIndex, bool rotated) =>
            TryTransferAt(source, destination, itemInstanceId, destinationIndex, rotated ? 1 : 0);

        public InventoryTransferResult TryTransferAt(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId,
            int destinationIndex,
            int rotationQuarterTurns)
        {
            if (source == null || destination == null)
                return TryTransfer(source, destination, itemInstanceId);

            if (source.Id == destination.Id)
                return CreateResult(false, false, InventoryTransferFailure.SameInventory,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    "Use the inventory's local move operation for items within the same inventory.");

            if (!source.TryGetItem(itemInstanceId, out Item item) || item == null)
                return CreateResult(false, false, InventoryTransferFailure.ItemNotFound,
                    InventoryOperationFailure.ItemNotFound, source, destination, itemInstanceId,
                    "The item is no longer present in the source inventory.");

            ISlotInventoryModel sourceSlots = source as ISlotInventoryModel;
            IGridInventoryModel sourceGrid = source as IGridInventoryModel;
            IEquipmentInventoryModel sourceEquipment = source as IEquipmentInventoryModel;
            string originalEquipmentSlot = null;
            bool hadEquipmentSlot = sourceEquipment != null && sourceEquipment.TryGetSlotOf(itemInstanceId, out originalEquipmentSlot);
            int originalSlot = -1;
            GridItemPlacement originalPlacement = default;
            bool hadSlot = sourceSlots != null && sourceSlots.TryGetSlotOf(itemInstanceId, out originalSlot);
            bool hadGridPlacement = sourceGrid != null && sourceGrid.TryGetPlacement(itemInstanceId, out originalPlacement);

            IGridInventoryModel destinationGrid = destination as IGridInventoryModel;
            ISlotInventoryModel destinationSlots = destination as ISlotInventoryModel;
            int gridX = -1;
            int gridY = -1;
            InventoryOperationFailure operationFailure;

            if (destinationGrid != null)
            {
                if (destinationIndex < 0 || destinationIndex >= destinationGrid.Capacity)
                    return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                        InventoryOperationFailure.OutOfBounds, source, destination, itemInstanceId,
                        "The selected grid cell is outside the destination grid.");
                gridX = destinationIndex % destinationGrid.Width;
                gridY = destinationIndex / destinationGrid.Width;
                if (!destinationGrid.CanPlaceAt(item, gridX, gridY, rotationQuarterTurns, out operationFailure))
                    return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                        operationFailure, source, destination, itemInstanceId,
                        "The selected grid area cannot accept this item: " + operationFailure + ".");
            }
            else if (destinationSlots != null)
            {
                if (!destinationSlots.CanAcceptAt(item, destinationIndex, out operationFailure))
                    return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                        operationFailure, source, destination, itemInstanceId,
                        "The selected destination slot cannot accept this item: " + operationFailure + ".");
            }
            else
            {
                return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    "The destination does not support targeted placement.");
            }

            if (!source.TryRemove(itemInstanceId, out Item removed, out operationFailure))
                return CreateResult(false, false, InventoryTransferFailure.SourceRemovalFailed,
                    operationFailure, source, destination, itemInstanceId,
                    "The source inventory could not remove the item.");

            bool added = destinationGrid != null
                ? destinationGrid.TryAddAt(removed, gridX, gridY, rotationQuarterTurns, out operationFailure)
                : destinationSlots.TryAddAt(removed, destinationIndex, out operationFailure);

            if (added)
                return CreateResult(true, false, InventoryTransferFailure.None,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    destinationGrid != null ? "Item transferred to the selected grid cell." :
                        "Item transferred to the selected slot.");

            bool restored = false;
            InventoryOperationFailure rollbackFailure = InventoryOperationFailure.None;
            if (hadGridPlacement && sourceGrid != null)
                restored = sourceGrid.TryAddAt(removed, originalPlacement.X, originalPlacement.Y,
                    originalPlacement.RotationQuarterTurns, out rollbackFailure);
            else if (hadEquipmentSlot && sourceEquipment != null)
                restored = sourceEquipment.TryAddAt(removed, originalEquipmentSlot, out rollbackFailure);
            else if (hadSlot && sourceSlots != null)
                restored = sourceSlots.TryAddAt(removed, originalSlot, out rollbackFailure);
            else
                restored = source.TryAdd(removed, out rollbackFailure);

            return CreateResult(false, false,
                restored ? InventoryTransferFailure.DestinationAddFailed : InventoryTransferFailure.RollbackFailed,
                restored ? operationFailure : rollbackFailure, source, destination, itemInstanceId,
                restored ? "Destination rejected the item; it was restored to its original location." :
                    "CRITICAL: Transfer failed and the source could not restore the original item location.");
        }

        /// <summary>Validates a transfer into one specifically named equipment slot without mutation.</summary>
        public InventoryTransferResult PreviewToEquipmentSlot(
            IInventoryModel source, IEquipmentInventoryModel destination,
            SerializableGuid itemInstanceId, string destinationSlotId)
        {
            if (source == null || destination == null)
                return CreateResult(false, true, source == null ? InventoryTransferFailure.InvalidSource : InventoryTransferFailure.InvalidDestination,
                    InventoryOperationFailure.None, source, destination, itemInstanceId, "A source and equipment destination are required.");
            if (source.Id == destination.Id)
                return CreateResult(false, true, InventoryTransferFailure.SameInventory, InventoryOperationFailure.None,
                    source, destination, itemInstanceId, "Source and destination are the same inventory.");
            if (!source.TryGetItem(itemInstanceId, out Item item) || item == null)
                return CreateResult(false, true, InventoryTransferFailure.ItemNotFound, InventoryOperationFailure.ItemNotFound,
                    source, destination, itemInstanceId, "The item is no longer present in the source inventory.");
            if (!destination.CanAcceptAt(item, destinationSlotId, out InventoryOperationFailure reason))
                return CreateResult(false, true, InventoryTransferFailure.DestinationRejected, reason,
                    source, destination, itemInstanceId, "The selected equipment slot cannot accept this item: " + reason + ".");
            return CreateResult(true, true, InventoryTransferFailure.None, InventoryOperationFailure.None,
                source, destination, itemInstanceId, "Equipment transfer is valid.");
        }

        /// <summary>Transfers an item to a named equipment slot and restores its original source placement on failure.</summary>
        public InventoryTransferResult TryTransferToEquipmentSlot(
            IInventoryModel source, IEquipmentInventoryModel destination,
            SerializableGuid itemInstanceId, string destinationSlotId)
        {
            InventoryTransferResult preview = PreviewToEquipmentSlot(source, destination, itemInstanceId, destinationSlotId);
            if (!preview.Success)
                return CreateResult(false, false, preview.Failure, preview.OperationFailure, source, destination,
                    itemInstanceId, preview.Message);

            ISlotInventoryModel sourceSlots = source as ISlotInventoryModel;
            IGridInventoryModel sourceGrid = source as IGridInventoryModel;
            IEquipmentInventoryModel sourceEquipment = source as IEquipmentInventoryModel;
            string originalEquipmentSlot = null;
            bool hadEquipmentSlot = sourceEquipment != null && sourceEquipment.TryGetSlotOf(itemInstanceId, out originalEquipmentSlot);
            int originalSlot = -1;
            GridItemPlacement originalPlacement = default;
            bool hadSlot = sourceSlots != null && sourceSlots.TryGetSlotOf(itemInstanceId, out originalSlot);
            bool hadGridPlacement = sourceGrid != null && sourceGrid.TryGetPlacement(itemInstanceId, out originalPlacement);

            if (!source.TryRemove(itemInstanceId, out Item removed, out InventoryOperationFailure failure))
                return CreateResult(false, false, InventoryTransferFailure.SourceRemovalFailed, failure,
                    source, destination, itemInstanceId, "The source inventory could not remove the item.");

            if (destination.TryAddAt(removed, destinationSlotId, out failure))
                return CreateResult(true, false, InventoryTransferFailure.None, InventoryOperationFailure.None,
                    source, destination, itemInstanceId, "Item equipped in slot '" + destinationSlotId + "'.");

            bool restored;
            InventoryOperationFailure rollbackFailure;
            if (hadGridPlacement && sourceGrid != null)
                restored = sourceGrid.TryAddAt(removed, originalPlacement.X, originalPlacement.Y, originalPlacement.RotationQuarterTurns, out rollbackFailure);
            else if (hadEquipmentSlot && sourceEquipment != null)
                restored = sourceEquipment.TryAddAt(removed, originalEquipmentSlot, out rollbackFailure);
            else if (hadSlot && sourceSlots != null)
                restored = sourceSlots.TryAddAt(removed, originalSlot, out rollbackFailure);
            else
                restored = source.TryAdd(removed, out rollbackFailure);

            return CreateResult(false, false,
                restored ? InventoryTransferFailure.DestinationAddFailed : InventoryTransferFailure.RollbackFailed,
                restored ? failure : rollbackFailure, source, destination, itemInstanceId,
                restored ? "Equipment slot rejected the item; it was restored to its original location." :
                    "CRITICAL: Equipment transfer failed and the source could not restore the original item location.");
        }

        private static bool TryValidate(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId,
            out Item item,
            out InventoryTransferFailure failure,
            out InventoryOperationFailure operationFailure,
            out string message)
        {
            item = null;
            operationFailure = InventoryOperationFailure.None;

            if (source == null)
            {
                failure = InventoryTransferFailure.InvalidSource;
                message = "A source inventory is required.";
                return false;
            }

            if (destination == null)
            {
                failure = InventoryTransferFailure.InvalidDestination;
                message = "A destination inventory is required.";
                return false;
            }

            if (source.Id == destination.Id)
            {
                failure = InventoryTransferFailure.SameInventory;
                message = "Source and destination are the same inventory.";
                return false;
            }

            if (itemInstanceId == SerializableGuid.Empty)
            {
                failure = InventoryTransferFailure.InvalidItemId;
                message = "A valid item instance ID is required.";
                return false;
            }

            if (!source.TryGetItem(itemInstanceId, out item) || item == null)
            {
                failure = InventoryTransferFailure.ItemNotFound;
                operationFailure = InventoryOperationFailure.ItemNotFound;
                message = "The item is no longer present in the source inventory.";
                return false;
            }

            if (!destination.CanAccept(item, out operationFailure))
            {
                failure = InventoryTransferFailure.DestinationRejected;
                message = "The destination cannot accept this item: " + operationFailure + ".";
                return false;
            }

            failure = InventoryTransferFailure.None;
            message = null;
            return true;
        }

        private static InventoryTransferResult CreateResult(
            bool success,
            bool isPreview,
            InventoryTransferFailure failure,
            InventoryOperationFailure operationFailure,
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId,
            string message)
        {
            return new InventoryTransferResult(
                success,
                isPreview,
                failure,
                operationFailure,
                source != null ? source.Id : SerializableGuid.Empty,
                destination != null ? destination.Id : SerializableGuid.Empty,
                itemInstanceId,
                message);
        }
    }
}
