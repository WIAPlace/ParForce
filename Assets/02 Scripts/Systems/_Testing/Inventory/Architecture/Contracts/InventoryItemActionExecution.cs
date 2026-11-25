namespace Systems.Inventory.Architecture
{
    /// <summary>Notification describing one attempted item action.</summary>
    public sealed class InventoryItemActionExecution
    {
        public IInventoryModel Inventory { get; }
        public Item Item { get; }
        public InventoryItemAction Action { get; }
        public InventoryItemActionResult Result { get; }

        internal InventoryItemActionExecution(IInventoryModel inventory, Item item,
            InventoryItemAction action, InventoryItemActionResult result)
        {
            Inventory = inventory;
            Item = item;
            Action = action;
            Result = result;
        }
    }
}
