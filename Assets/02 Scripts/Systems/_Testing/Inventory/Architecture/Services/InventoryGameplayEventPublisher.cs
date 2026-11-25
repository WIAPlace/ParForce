using System;

namespace Systems.Inventory.Architecture
{
    /// <summary>Translates successful operation results into semantic gameplay notifications.</summary>
    internal static class InventoryGameplayEventPublisher
    {
        internal static void Publish(InventoryOperationResult result, Action<InventoryGameplayEvent> raise)
        {
            if (result == null || !result.Success || raise == null) return;

            foreach (InventoryItemChange change in result.ItemChanges)
            {
                InventoryGameplayEventKind kind;
                switch (result.Operation)
                {
                    case InventoryOperationKind.Add:
                    case InventoryOperationKind.AddAt:
                        kind = InventoryGameplayEventKind.ItemAdded;
                        break;
                    case InventoryOperationKind.Remove:
                    case InventoryOperationKind.RemoveAll:
                        kind = InventoryGameplayEventKind.ItemRemoved;
                        break;
                    case InventoryOperationKind.Move:
                    case InventoryOperationKind.MoveTo:
                        kind = InventoryGameplayEventKind.ItemMoved;
                        break;
                    default:
                        kind = change.ResultingQuantity <= 0 && change.PreviousQuantity > 0
                            ? InventoryGameplayEventKind.ItemRemoved
                            : InventoryGameplayEventKind.ItemQuantityChanged;
                        break;
                }

                // Ignore successful no-op quantity assignments and empty move requests.
                if (kind == InventoryGameplayEventKind.ItemQuantityChanged &&
                    change.PreviousQuantity == change.ResultingQuantity) continue;
                raise(new InventoryGameplayEvent(result.InventoryId, kind, change, result));
            }
        }
    }
}
