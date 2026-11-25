using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Simple example action that logs an item's authored name and description.</summary>
    [CreateAssetMenu(fileName = "Inspect Item Action", menuName = "Inventory/Item Actions/Inspect")]
    public sealed class InventoryItemInspectAction : InventoryItemAction
    {
        public override InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context)
        {
            bool valid = context.Item != null && context.Item.details != null;
            return valid
                ? InventoryItemActionAvailability.Available()
                : InventoryItemActionAvailability.Unavailable("There is no item to inspect.");
        }

        public override InventoryItemActionResult Execute(InventoryItemActionContext context)
        {
            string itemName = string.IsNullOrWhiteSpace(context.Item.details.Name)
                ? context.Item.details.name : context.Item.details.Name;
            string description = string.IsNullOrWhiteSpace(context.Item.details.Description)
                ? "No description available." : context.Item.details.Description;
            Debug.Log($"{itemName}: {description}");
            return InventoryItemActionResult.Succeeded("Inspected " + itemName + ".");
        }
    }
}
