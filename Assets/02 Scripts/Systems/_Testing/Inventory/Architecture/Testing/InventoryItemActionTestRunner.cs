using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Verifies item action discovery, availability checks, execution, and stale-item safety.</summary>
    public sealed class InventoryItemActionTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Inventory Item Action Tests")]
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

            ItemDetails definition = ScriptableObject.CreateInstance<ItemDetails>();
            definition.Name = "Action Test Item";
            definition.maxStack = 5;
            TestRemoveOneAction action = ScriptableObject.CreateInstance<TestRemoveOneAction>();

            try
            {
                var inventory = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Action Test", 4);
                Item item = definition.Create(2);
                inventory.AddAt(item, 0);
                definition.Actions.Add(action);
                var service = new InventoryItemActionService();
                int executedEvents = 0;
                service.ActionExecuted += _ => executedEvents++;

                IReadOnlyList<InventoryItemAction> actions = service.GetConfiguredActions(inventory, item.Id);
                Check("Configured actions are discovered from the item definition", actions.Count == 1 && actions[0] == action);
                Check("An available action passes its availability check", service.CanExecute(inventory, item.Id, action, out _));

                InventoryItemActionExecution execution = service.Execute(inventory, item.Id, action);
                Check("Executing an action returns success and performs its inventory operation",
                    execution.Result.Success && inventory.TryGetItem(item.Id, out Item remaining) &&
                    remaining.quantity == 1 && executedEvents == 1);

                action.SetUnavailable(true);
                InventoryItemActionAvailability unavailableState = service.GetAvailability(inventory, item.Id, action);
                Check("Unavailable actions provide a reason for the context menu",
                    !unavailableState.IsAvailable && unavailableState.Reason == "Disabled for the test.");
                InventoryItemActionExecution unavailable = service.Execute(inventory, item.Id, action);
                Check("Unavailable actions do not execute", !unavailable.Result.Success &&
                    inventory.TryGetItem(item.Id, out Item unchanged) && unchanged.quantity == 1);

                inventory.Remove(item.Id);
                InventoryItemActionExecution stale = service.Execute(inventory, item.Id, action);
                Check("Actions cannot execute after their item has left the inventory", !stale.Result.Success);
            }
            finally
            {
                Object.DestroyImmediate(action);
                Object.DestroyImmediate(definition);
            }

            Debug.Log($"INVENTORY ITEM ACTION TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more inventory item action tests failed.", this);
        }
    }

}
