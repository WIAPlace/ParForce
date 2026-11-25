using System;
using System.Collections.Generic;

namespace Systems.Inventory.Architecture
{
    /// <summary>Optional relay for gameplay systems that listen to several inventories.</summary>
    /// <remarks>This is an ordinary C# object, not a static singleton. Create it with the lifetime
    /// of the player/session and register the inventories whose events should be observed.</remarks>
    public sealed class InventoryGameplayEventBus : IDisposable
    {
        private readonly HashSet<IInventoryModel> registered = new HashSet<IInventoryModel>();

        public event Action<InventoryGameplayEvent> EventRaised;
        public int RegisteredInventoryCount => registered.Count;

        public void Register(IInventoryModel inventory)
        {
            if (inventory == null || !registered.Add(inventory)) return;
            inventory.GameplayEventRaised += Relay;
        }

        public void Unregister(IInventoryModel inventory)
        {
            if (inventory == null || !registered.Remove(inventory)) return;
            inventory.GameplayEventRaised -= Relay;
        }

        public void Clear()
        {
            foreach (IInventoryModel inventory in registered)
                inventory.GameplayEventRaised -= Relay;
            registered.Clear();
        }

        private void Relay(InventoryGameplayEvent gameplayEvent) => EventRaised?.Invoke(gameplayEvent);

        public void Dispose() => Clear();
    }
}
