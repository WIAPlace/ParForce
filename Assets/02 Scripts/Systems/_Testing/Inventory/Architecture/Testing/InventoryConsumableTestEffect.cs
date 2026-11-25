using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Test-only effect that records successful applications.</summary>
    public sealed class InventoryConsumableTestEffect : InventoryConsumableEffect
    {
        public int AppliedCount { get; private set; }
        public bool Available { get; set; } = true;

        public override InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context) =>
            Available ? InventoryItemActionAvailability.Available() :
                InventoryItemActionAvailability.Unavailable("The test effect is unavailable.");

        public override InventoryItemActionResult Apply(InventoryItemActionContext context)
        {
            AppliedCount++;
            return InventoryItemActionResult.Succeeded();
        }
    }
}
