namespace Systems.Inventory.Architecture
{
    public enum InventoryTransferFailure
    {
        None,
        InvalidSource,
        InvalidDestination,
        InvalidItemId,
        ItemNotFound,
        SameInventory,
        DestinationRejected,
        SourceRemovalFailed,
        DestinationAddFailed,
        RollbackFailed
    }

    /// <summary>
    /// Outcome of validating or performing a cross-inventory transfer.
    /// OperationFailure carries the destination/source model's more specific
    /// rejection reason when one is available.
    /// </summary>
    public sealed class InventoryTransferResult
    {
        public bool Success { get; }
        public bool IsPreview { get; }
        public InventoryTransferFailure Failure { get; }
        public InventoryOperationFailure OperationFailure { get; }
        public SerializableGuid SourceInventoryId { get; }
        public SerializableGuid DestinationInventoryId { get; }
        public SerializableGuid ItemInstanceId { get; }
        public string Message { get; }

        internal InventoryTransferResult(
            bool success,
            bool isPreview,
            InventoryTransferFailure failure,
            InventoryOperationFailure operationFailure,
            SerializableGuid sourceInventoryId,
            SerializableGuid destinationInventoryId,
            SerializableGuid itemInstanceId,
            string message)
        {
            Success = success;
            IsPreview = isPreview;
            Failure = failure;
            OperationFailure = operationFailure;
            SourceInventoryId = sourceInventoryId;
            DestinationInventoryId = destinationInventoryId;
            ItemInstanceId = itemInstanceId;
            Message = message;
        }
    }
}
