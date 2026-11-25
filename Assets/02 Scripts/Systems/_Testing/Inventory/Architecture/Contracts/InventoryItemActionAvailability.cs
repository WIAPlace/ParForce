namespace Systems.Inventory.Architecture
{
    /// <summary>Current availability of an item action, including a player-facing explanation.</summary>
    public sealed class InventoryItemActionAvailability
    {
        public bool IsAvailable { get; }
        public string Reason { get; }

        private InventoryItemActionAvailability(bool isAvailable, string reason)
        {
            IsAvailable = isAvailable;
            Reason = reason ?? string.Empty;
        }

        public static InventoryItemActionAvailability Available() =>
            new InventoryItemActionAvailability(true, string.Empty);

        public static InventoryItemActionAvailability Unavailable(string reason) =>
            new InventoryItemActionAvailability(false, reason);
    }
}
