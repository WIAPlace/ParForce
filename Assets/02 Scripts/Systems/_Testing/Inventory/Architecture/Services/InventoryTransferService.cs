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
            if (source.TryAdd(removedItem, out rollbackFailure))
                return CreateResult(false, false,
                    InventoryTransferFailure.DestinationAddFailed, operationFailure,
                    source, destination, itemInstanceId,
                    "The destination rejected the item. It was restored to the source.");

            return CreateResult(false, false, InventoryTransferFailure.RollbackFailed,
                rollbackFailure, source, destination, itemInstanceId,
                "CRITICAL: The destination rejected the item and the source could not restore it. " +
                "The inventories require reconciliation.");
        }

        /// <summary>Transfers an item into an explicitly selected empty slot.</summary>
        public InventoryTransferResult TryTransferAt(
            IInventoryModel source,
            IInventoryModel destination,
            SerializableGuid itemInstanceId,
            int destinationSlot)
        {
            if (source == null || destination == null)
                return TryTransfer(source, destination, itemInstanceId);

            if (source.Id == destination.Id)
                return CreateResult(false, false, InventoryTransferFailure.SameInventory,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    "Use the inventory's local move operation for items within the same inventory.");

            if (!(destination is ISlotInventoryModel destinationSlots) ||
                !(source is ISlotInventoryModel sourceSlots))
                return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    "Both inventories must support indexed slots for a targeted transfer.");

            if (!source.TryGetItem(itemInstanceId, out Item item) || item == null)
                return CreateResult(false, false, InventoryTransferFailure.ItemNotFound,
                    InventoryOperationFailure.ItemNotFound, source, destination, itemInstanceId,
                    "The item is no longer present in the source inventory.");

            if (!destinationSlots.CanAcceptAt(item, destinationSlot, out InventoryOperationFailure opFailure))
                return CreateResult(false, false, InventoryTransferFailure.DestinationRejected,
                    opFailure, source, destination, itemInstanceId,
                    "The selected destination slot cannot accept this item: " + opFailure + ".");

            sourceSlots.TryGetSlotOf(itemInstanceId, out int originalSlot);
            if (!source.TryRemove(itemInstanceId, out Item removed, out opFailure))
                return CreateResult(false, false, InventoryTransferFailure.SourceRemovalFailed,
                    opFailure, source, destination, itemInstanceId,
                    "The source inventory could not remove the item.");

            if (destinationSlots.TryAddAt(removed, destinationSlot, out opFailure))
                return CreateResult(true, false, InventoryTransferFailure.None,
                    InventoryOperationFailure.None, source, destination, itemInstanceId,
                    "Item transferred to the selected slot.");

            InventoryOperationFailure rollbackFailure;
            bool restored = sourceSlots.TryAddAt(removed, originalSlot, out rollbackFailure);
            return CreateResult(false, false,
                restored ? InventoryTransferFailure.DestinationAddFailed : InventoryTransferFailure.RollbackFailed,
                restored ? opFailure : rollbackFailure, source, destination, itemInstanceId,
                restored ? "Destination rejected the item; it was restored to its original slot." :
                    "CRITICAL: Transfer failed and the source could not restore the original slot.");
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
