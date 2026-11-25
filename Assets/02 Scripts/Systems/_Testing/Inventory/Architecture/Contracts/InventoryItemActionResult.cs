namespace Systems.Inventory.Architecture
{
    /// <summary>Outcome of executing an item action. This is separate from inventory mutation results.</summary>
    public sealed class InventoryItemActionResult
    {
        public bool Success { get; }
        public string Message { get; }

        private InventoryItemActionResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        public static InventoryItemActionResult Succeeded(string message = null) =>
            new InventoryItemActionResult(true, message);

        public static InventoryItemActionResult Failed(string message) =>
            new InventoryItemActionResult(false, message);
    }
}
