using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Checks gameplay event semantics without opening an inventory panel.</summary>
    public sealed class InventoryGameplayEventTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Inventory Gameplay Event Tests")]
        public void RunTests()
        {
            int passed = 0;
            int failed = 0;
            var report = new List<string>();
            void Check(string name, bool condition)
            {
                report.Add($"{(condition ? "PASS" : "FAIL")}: {name}");
                if (condition) passed++; else failed++;
            }

            ItemDetails potion = ScriptableObject.CreateInstance<ItemDetails>();
            potion.Name = "Gameplay Event Test Potion";
            potion.category = ItemCategory.General;
            potion.maxStack = 10;

            try
            {
                var inventory = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Event Test", 6);
                var received = new List<InventoryGameplayEvent>();
                inventory.GameplayEventRaised += received.Add;

                Item first = potion.Create(5);
                InventoryOperationResult add = inventory.AddAt(first, 0);
                Check("Adding a stack emits one ItemAdded event with its quantity", add.Success &&
                    received.Count == 1 && received[0].Kind == InventoryGameplayEventKind.ItemAdded &&
                    received[0].QuantityDelta == 5 && received[0].ResultingQuantity == 5);

                InventoryOperationResult set = inventory.SetQuantity(first.Id, 3);
                Check("Changing a stack emits ItemQuantityChanged with the correct delta", set.Success &&
                    received.Count == 2 && received[1].Kind == InventoryGameplayEventKind.ItemQuantityChanged &&
                    received[1].QuantityDelta == -2 && received[1].PreviousQuantity == 5 &&
                    received[1].ResultingQuantity == 3);

                InventoryOperationResult remove = inventory.Remove(first.Id);
                Check("Removing the final stack emits ItemRemoved", remove.Success && received.Count == 3 &&
                    received[2].Kind == InventoryGameplayEventKind.ItemRemoved &&
                    received[2].PreviousQuantity == 3 && received[2].ResultingQuantity == 0);

                int eventCountBeforeFailure = received.Count;
                InventoryOperationResult failedResult = inventory.Remove(SerializableGuid.NewGuid());
                Check("Failed operations emit no gameplay events", !failedResult.Success && received.Count == eventCountBeforeFailure);

                Item stackA = potion.Create(2);
                Item stackB = potion.Create(4);
                inventory.AddAt(stackA, 0);
                inventory.AddAt(stackB, 1);
                int beforeMultiStackRemoval = received.Count;
                InventoryOperationResult multi = inventory.RemoveQuantityExact(potion, 3);
                int emittedForMulti = received.Count - beforeMultiStackRemoval;
                Check("Multi-stack removal emits one event per affected stack", multi.Success &&
                    multi.AffectedQuantity == 3 && multi.ItemChanges.Count == 2 && emittedForMulti == 2);

                var secondInventory = new GridInventoryModel(SerializableGuid.NewGuid(), "Bus Test", 2, 2);
                using (var bus = new InventoryGameplayEventBus())
                {
                    int relayed = 0;
                    bus.EventRaised += _ => relayed++;
                    bus.Register(inventory);
                    bus.Register(secondInventory);
                    bus.Register(inventory); // Duplicate registration must not duplicate delivery.
                    secondInventory.Add(potion.Create(1));
                    Check("The event bus relays registered inventory events once", relayed == 1 &&
                        bus.RegisteredInventoryCount == 2);
                    bus.Unregister(secondInventory);
                    secondInventory.Add(potion.Create(1));
                    Check("Unregistering stops relaying that inventory's events", relayed == 1);
                }
            }
            finally
            {
                Object.DestroyImmediate(potion);
            }

            Debug.Log($"INVENTORY GAMEPLAY EVENT TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more inventory gameplay event tests failed.", this);
        }
    }
}
