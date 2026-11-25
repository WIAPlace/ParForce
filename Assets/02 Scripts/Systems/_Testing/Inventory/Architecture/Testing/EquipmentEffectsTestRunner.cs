using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Runs equipment-to-attribute integration checks without a player or scene character.</summary>
    public sealed class EquipmentEffectsTestRunner : MonoBehaviour
    {
        [ContextMenu("Run Equipment Effects Tests")]
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

            ItemDetails helmetDefinition = CreateDefinition("Test Helmet", EquipmentType.Head,
                new EquipmentEffectDefinition("Defense", 5f));
            ItemDetails ringDefinition = CreateDefinition("Test Ring", EquipmentType.Ring,
                new EquipmentEffectDefinition("Defense", 2f));
            ItemDetails secondRingDefinition = CreateDefinition("Test Ring Plus", EquipmentType.Ring,
                new EquipmentEffectDefinition("Defense", 3f));
            ItemDetails replacementRingDefinition = CreateDefinition("Replacement Ring", EquipmentType.Ring,
                new EquipmentEffectDefinition("Defense", 4f));

            try
            {
                var slots = new List<EquipmentSlotDefinition>
                {
                    new EquipmentSlotDefinition("Head", "Head", new[] { EquipmentType.Head }),
                    new EquipmentSlotDefinition("RingLeft", "Left Ring", new[] { EquipmentType.Ring }),
                    new EquipmentSlotDefinition("RingRight", "Right Ring", new[] { EquipmentType.Ring }),
                    new EquipmentSlotDefinition("RingSpare", "Spare Ring", new[] { EquipmentType.Ring })
                };
                var equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Test Equipment", slots);
                var attributes = new AttributeModel();
                attributes.SetBaseValue("Defense", 10f);
                using (var effects = new EquipmentEffectService(equipment, attributes))
                {
                    Item helmet = helmetDefinition.Create(1);
                    Item ringA = ringDefinition.Create(1);
                    Item ringB = secondRingDefinition.Create(1);
                    Item ringC = replacementRingDefinition.Create(1);

                    bool helmetEquipped = equipment.AddAt(helmet, "Head").Success;
                    Check("Equipping an item applies its modifier", helmetEquipped && Mathf.Approximately(attributes.GetValue("Defense"), 15f));

                    bool ringAEquipped = equipment.AddAt(ringA, "RingLeft").Success;
                    bool ringBEquipped = equipment.AddAt(ringB, "RingRight").Success;
                    Check("Modifiers from multiple items stack", ringAEquipped && ringBEquipped &&
                        Mathf.Approximately(attributes.GetValue("Defense"), 20f));

                    bool moved = equipment.Move(ringA.Id, "RingSpare").Success;
                    Check("Moving an equipped item between slots does not duplicate or remove its effect", moved &&
                        Mathf.Approximately(attributes.GetValue("Defense"), 20f) && attributes.GetModifierCount(ringA.Id) == 1);

                    bool removedRing = equipment.Remove(ringA.Id).Success;
                    Check("Removing one item removes only that item's modifiers", removedRing &&
                        Mathf.Approximately(attributes.GetValue("Defense"), 18f) && attributes.GetModifierCount(ringB.Id) == 1);

                    bool replaced = equipment.Remove(ringB.Id).Success && equipment.AddAt(ringC, "RingLeft").Success;
                    Check("Replacing an item removes the old source and applies the new source", replaced &&
                        Mathf.Approximately(attributes.GetValue("Defense"), 19f) && attributes.GetModifierCount(ringB.Id) == 0 &&
                        attributes.GetModifierCount(ringC.Id) == 1);

                    bool removedHelmet = equipment.Remove(helmet.Id).Success;
                    Check("Unequipping the helmet leaves the remaining ring modifier intact", removedHelmet &&
                        Mathf.Approximately(attributes.GetValue("Defense"), 14f));
                }

                Check("Disposing the effect service cleans up its active modifiers", Mathf.Approximately(attributes.GetValue("Defense"), 10f) &&
                    attributes.ActiveSourceCount == 0);

                var percentageAttributes = new AttributeModel();
                percentageAttributes.SetBaseValue("Health", 100f);
                SerializableGuid source = SerializableGuid.NewGuid();
                percentageAttributes.SetModifiersFromSource(source, new[]
                {
                    new AttributeModifier(source, "Health", 10f, AttributeModifierOperation.Flat),
                    new AttributeModifier(source, "Health", 0.2f, AttributeModifierOperation.AdditivePercent)
                });
                Check("Attribute model combines flat and percentage modifiers", Mathf.Approximately(percentageAttributes.GetValue("Health"), 132f));
            }
            finally
            {
                DestroyTestObject(helmetDefinition);
                DestroyTestObject(ringDefinition);
                DestroyTestObject(secondRingDefinition);
                DestroyTestObject(replacementRingDefinition);
            }

            Debug.Log($"EQUIPMENT EFFECT TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more equipment effect tests failed.", this);
        }

        private static ItemDetails CreateDefinition(string itemName, EquipmentType type,
            params EquipmentEffectDefinition[] effects)
        {
            ItemDetails definition = ScriptableObject.CreateInstance<ItemDetails>();
            definition.Name = itemName;
            definition.category = ItemCategory.Equipment;
            definition.equipmentType = type;
            definition.EquipmentEffects = new List<EquipmentEffectDefinition>(effects);
            return definition;
        }

        private static void DestroyTestObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
