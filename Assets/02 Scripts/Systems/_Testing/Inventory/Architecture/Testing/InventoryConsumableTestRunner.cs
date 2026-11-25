using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Checks consumable availability, effect application, and exact one-unit consumption.</summary>
    public sealed class InventoryConsumableTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Inventory Consumable Tests")]
        public void RunTests()
        {
            int passed = 0;
            int failed = 0;
            var report = new List<string>();
            void Check(string label, bool condition)
            {
                report.Add($"{(condition ? "PASS" : "FAIL")}: {label}");
                if (condition) passed++; else failed++;
            }

            ItemDetails definition = ScriptableObject.CreateInstance<ItemDetails>();
            definition.Name = "Test Potion";
            definition.maxStack = 5;
            InventoryItemConsumeAction action = ScriptableObject.CreateInstance<InventoryItemConsumeAction>();
            InventoryConsumableTestEffect effect = ScriptableObject.CreateInstance<InventoryConsumableTestEffect>();
            action.Effects.Add(effect);
            try
            {
                var inventory = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Consumable Test", 4);
                Item item = definition.Create(2);
                inventory.AddAt(item, 0);
                definition.Actions.Add(action);
                var service = new InventoryItemActionService();

                Check("Consumable is available with quantity and a valid effect",
                    service.GetAvailability(inventory, item.Id, action).IsAvailable);

                effect.Available = false;
                InventoryItemActionAvailability unavailable = service.GetAvailability(inventory, item.Id, action);
                Check("Unavailable effects disable the consume action and explain why",
                    !unavailable.IsAvailable && unavailable.Reason == "The test effect is unavailable.");
                Check("Unavailable consumable does not apply effects or consume quantity",
                    !service.Execute(inventory, item.Id, action).Result.Success && effect.AppliedCount == 0 &&
                    inventory.TryGetItem(item.Id, out Item unchanged) && unchanged.quantity == 2);

                effect.Available = true;
                InventoryItemActionExecution execution = service.Execute(inventory, item.Id, action);
                Check("Successful consume applies effects once and removes exactly one unit",
                    execution.Result.Success && effect.AppliedCount == 1 &&
                    inventory.TryGetItem(item.Id, out Item remaining) && remaining.quantity == 1);

                InventoryItemActionExecution second = service.Execute(inventory, item.Id, action);
                Check("Repeated consumption consumes the final unit and removes the stack",
                    second.Result.Success && effect.AppliedCount == 2 && !inventory.TryGetItem(item.Id, out _));
            }
            finally
            {
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(action);
                Object.DestroyImmediate(definition);
            }

            Debug.Log($"INVENTORY CONSUMABLE TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more inventory consumable tests failed.", this);
        }
    }
}
