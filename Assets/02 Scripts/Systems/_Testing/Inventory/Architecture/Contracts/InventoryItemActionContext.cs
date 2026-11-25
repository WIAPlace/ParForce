using System;

namespace Systems.Inventory.Architecture
{
    /// <summary>Runtime information supplied to an item action when it is evaluated or executed.</summary>
    public readonly struct InventoryItemActionContext
    {
        public IInventoryModel Inventory { get; }
        public Item Item { get; }

        public InventoryItemActionContext(IInventoryModel inventory, Item item)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }
    }
}
