using System;
using System.Collections.Generic;
using System.Linq;

namespace Systems.Inventory.Architecture
{
    /// <summary>Discovers, validates, and executes actions configured on an item's definition.</summary>
    public sealed class InventoryItemActionService
    {
        public event Action<InventoryItemActionExecution> ActionExecuted;

        public IReadOnlyList<InventoryItemAction> GetConfiguredActions(IInventoryModel inventory, SerializableGuid itemId)
        {
            if (!TryCreateContext(inventory, itemId, out InventoryItemActionContext context))
                return Array.Empty<InventoryItemAction>();

            return context.Item.details != null && context.Item.details.Actions != null
                ? context.Item.details.Actions.Where(action => action != null)
                    .OrderBy(action => action.Priority).ThenBy(action => action.DisplayName).ToArray()
                : Array.Empty<InventoryItemAction>();
        }

        public InventoryItemActionAvailability GetAvailability(IInventoryModel inventory, SerializableGuid itemId,
            InventoryItemAction action)
        {
            if (action == null || !TryCreateContext(inventory, itemId, out InventoryItemActionContext context))
                return InventoryItemActionAvailability.Unavailable("The item or action is no longer available.");
            if (context.Item.details.Actions == null || !context.Item.details.Actions.Contains(action))
                return InventoryItemActionAvailability.Unavailable("This action is not configured for the item.");
            try
            {
                return action.GetAvailability(context) ??
                    InventoryItemActionAvailability.Unavailable("The action returned no availability result.");
            }
            catch (Exception exception)
            {
                return InventoryItemActionAvailability.Unavailable("Action validation failed: " + exception.Message);
            }
        }

        public bool CanExecute(IInventoryModel inventory, SerializableGuid itemId,
            InventoryItemAction action, out string reason)
        {
            InventoryItemActionAvailability availability = GetAvailability(inventory, itemId, action);
            reason = availability.Reason;
            return availability.IsAvailable;
        }

        public InventoryItemActionExecution Execute(IInventoryModel inventory, SerializableGuid itemId,
            InventoryItemAction action)
        {
            Item item = null;
            InventoryItemActionResult result;
            if (action == null || !TryCreateContext(inventory, itemId, out InventoryItemActionContext context))
            {
                result = InventoryItemActionResult.Failed("The item or action is no longer available.");
            }
            else
            {
                item = context.Item;
                if (!CanExecute(inventory, itemId, action, out string reason))
                    result = InventoryItemActionResult.Failed(reason);
                else
                {
                    try
                    {
                        result = action.Execute(context) ??
                            InventoryItemActionResult.Failed("The action returned no result.");
                    }
                    catch (Exception exception)
                    {
                        result = InventoryItemActionResult.Failed("Action execution failed: " + exception.Message);
                    }
                }
            }

            InventoryItemActionExecution execution = new InventoryItemActionExecution(inventory, item, action, result);
            ActionExecuted?.Invoke(execution);
            return execution;
        }

        private static bool TryCreateContext(IInventoryModel inventory, SerializableGuid itemId,
            out InventoryItemActionContext context)
        {
            context = default;
            if (inventory == null || !inventory.TryGetItem(itemId, out Item item) ||
                item == null || item.details == null) return false;
            context = new InventoryItemActionContext(inventory, item);
            return true;
        }
    }
}
