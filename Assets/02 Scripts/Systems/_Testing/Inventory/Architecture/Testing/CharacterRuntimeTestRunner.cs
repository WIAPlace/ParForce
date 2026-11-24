using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Verifies independent character runtime owners without creating scene characters.</summary>
    public sealed class CharacterRuntimeTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Character Runtime Tests")]
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

            ItemDetails armorDefinition = ScriptableObject.CreateInstance<ItemDetails>();
            armorDefinition.Name = "Runtime Test Armor";
            armorDefinition.category = ItemCategory.Equipment;
            armorDefinition.equipmentType = EquipmentType.Chest;
            armorDefinition.EquipmentEffects = new List<EquipmentEffectDefinition>
            {
                new EquipmentEffectDefinition("Defense", 7f)
            };

            var slots = new List<EquipmentSlotDefinition>
            {
                new EquipmentSlotDefinition("Chest", "Chest", new[] { EquipmentType.Chest })
            };

            try
            {
                var baseValues = new Dictionary<string, float>
                {
                    { "Health", 100f },
                    { "Defense", 10f }
                };
                using (var first = new CharacterRuntimeModel(SerializableGuid.NewGuid(), "First", slots, baseValues))
                using (var second = new CharacterRuntimeModel(SerializableGuid.NewGuid(), "Second", slots, baseValues))
                {
                    int firstDefenseNotifications = 0;
                    first.AttributeChanged += (id, value) =>
                    {
                        if (id == "Defense") firstDefenseNotifications++;
                    };

                    Item firstArmor = armorDefinition.Create(1);
                    bool equipped = first.Equipment.TryAddAt(firstArmor, "Chest", out _);
                    Check("Equipping an item updates the owning runtime's attributes", equipped &&
                        Mathf.Approximately(first.GetAttribute("Defense"), 17f));

                    Check("A second runtime owner keeps its own independent attributes", 
                        Mathf.Approximately(second.GetAttribute("Defense"), 10f) &&
                        Mathf.Approximately(second.GetAttribute("Health"), 100f));

                    first.SetBaseAttribute("Defense", 20f);
                    Check("Changing one owner's base attribute preserves its equipment modifiers and does not affect another owner",
                        Mathf.Approximately(first.GetAttribute("Defense"), 27f) &&
                        Mathf.Approximately(second.GetAttribute("Defense"), 10f));

                    Check("Attribute changes are observable through the runtime owner", firstDefenseNotifications >= 2);

                    SerializableGuid secondId = second.Id;
                    Check("Runtime owners have distinct identities and independent equipment inventories",
                        first.Id != secondId && first.Equipment.Id != second.Equipment.Id && second.Equipment.ItemCount == 0);
                }

                var disposable = new CharacterRuntimeModel(SerializableGuid.NewGuid(), "Disposable", slots,
                    new Dictionary<string, float> { { "Defense", 4f } });
                Item disposableArmor = armorDefinition.Create(1);
                disposable.Equipment.TryAddAt(disposableArmor, "Chest", out _);
                disposable.Dispose();
                Check("Disposing a runtime owner removes its active equipment modifiers",
                    disposable.Attributes.ActiveSourceCount == 0 && Mathf.Approximately(disposable.Attributes.GetValue("Defense"), 4f));
            }
            finally
            {
                if (Application.isPlaying) Destroy(armorDefinition);
                else DestroyImmediate(armorDefinition);
            }

            Debug.Log($"CHARACTER RUNTIME TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more character runtime tests failed.", this);
        }
    }
}
