namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Reusable effect definition for a consumable. Keep definitions stateless; store runtime
    /// state in the systems affected by the effect.
    /// </summary>
    public abstract class InventoryConsumableEffect : UnityEngine.ScriptableObject
    {
        public abstract InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context);
        /// <summary>Apply after all configured effects have passed availability validation.</summary>
        public abstract InventoryItemActionResult Apply(InventoryItemActionContext context);
    }
}
