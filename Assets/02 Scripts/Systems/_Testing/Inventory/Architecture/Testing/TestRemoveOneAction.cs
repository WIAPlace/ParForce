namespace Systems.Inventory.Architecture
{
    /// <summary>Test-only action used by InventoryItemActionTestRunner.</summary>
    public sealed class TestRemoveOneAction : InventoryItemAction
    {
        private bool unavailable;

        public void SetUnavailable(bool value) => unavailable = value;

        public override InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context)
        {
            if (unavailable) return InventoryItemActionAvailability.Unavailable("Disabled for the test.");
            if (context.Item == null || context.Item.quantity <= 0)
                return InventoryItemActionAvailability.Unavailable("There is no quantity to remove.");
            return InventoryItemActionAvailability.Available();
        }

        public override InventoryItemActionResult Execute(InventoryItemActionContext context)
        {
            InventoryOperationResult remove = context.Inventory.RemoveQuantityUpTo(context.Item.Id, 1);
            return remove.Success
                ? InventoryItemActionResult.Succeeded("Removed one unit.")
                : InventoryItemActionResult.Failed(remove.Failure.ToString());
        }
    }
}
