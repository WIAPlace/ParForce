using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>Isolated checks for named equipment slots and equipment transfer rules.</summary>
    public sealed class EquipmentInventoryTestRunner : MonoBehaviour
    {
        [SerializeField] private EquipmentSlotLayout layout;
        [SerializeField] private ItemDetails helmetDefinition;
        [SerializeField] private ItemDetails ringDefinition;
        [SerializeField] private ItemDetails potionDefinition;

        [ContextMenu("Run Equipment Inventory Tests")]
        public void RunTests()
        {
            if (layout == null || helmetDefinition == null || ringDefinition == null || potionDefinition == null)
            {
                Debug.LogError("Assign an EquipmentSlotLayout and helmet, ring, and potion ItemDetails assets.", this);
                return;
            }

            int passed = 0;
            int failed = 0;
            var report = new List<string>();
            void Check(string name, bool condition)
            {
                report.Add($"{(condition ? "PASS" : "FAIL")}: {name}");
                if (condition) passed++; else failed++;
            }

            var equipment = new EquipmentInventoryModel(SerializableGuid.NewGuid(), "Test Equipment", layout.Slots);
            var backpack = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Test Backpack", 6);
            var transfers = new InventoryTransferService();
            Item helmet = helmetDefinition.Create(1);
            Item ringA = ringDefinition.Create(1);
            Item ringB = ringDefinition.Create(1);
            Item potion = potionDefinition.Create(1);
            backpack.TryAddAt(helmet, 0, out _);
            backpack.TryAddAt(ringA, 1, out _);
            backpack.TryAddAt(ringB, 2, out _);
            backpack.TryAddAt(potion, 3, out _);

            InventoryTransferResult helmetTransfer = transfers.TryTransferToEquipmentSlot(backpack, equipment, helmet.Id, "Head");
            Check("Helmet equips into the Head slot", helmetTransfer.Success && equipment.TryGetItemAt("Head", out Item head) && ReferenceEquals(head, helmet));
            InventoryTransferResult ringLeft = transfers.TryTransferToEquipmentSlot(backpack, equipment, ringA.Id, "RingLeft");
            bool ringLocalMove = equipment.TryMove(ringA.Id, "RingRight", out _);
            bool ringMoveBack = equipment.TryMove(ringA.Id, "RingLeft", out _);
            InventoryTransferResult ringRight = transfers.TryTransferToEquipmentSlot(backpack, equipment, ringB.Id, "RingRight");
            Check("Two rings occupy independently identified slots", ringLeft.Success && ringRight.Success &&
                equipment.TryGetItemAt("RingLeft", out Item left) && ReferenceEquals(left, ringA) &&
                equipment.TryGetItemAt("RingRight", out Item right) && ReferenceEquals(right, ringB));
            Check("An item can move between compatible empty named slots", ringLocalMove && ringMoveBack);

            InventoryTransferResult potionPreview = transfers.PreviewToEquipmentSlot(backpack, equipment, potion.Id, "Chest");
            Check("Non-equipment item is rejected by equipment slots", !potionPreview.Success &&
                potionPreview.OperationFailure == InventoryOperationFailure.IncompatibleItemType &&
                backpack.TryGetItem(potion.Id, out _));
            InventoryTransferResult occupied = transfers.TryTransferToEquipmentSlot(backpack, equipment, potion.Id, "Head");
            Check("Occupied equipment slot rejects transfer without losing source item", !occupied.Success && backpack.TryGetItem(potion.Id, out _));

            bool localMove = equipment.TryMove(ringA.Id, "RingRight", out _);
            Check("Equipment local move rejects occupied target", !localMove &&
                equipment.TryGetItemAt("RingLeft", out Item stillLeft) && ReferenceEquals(stillLeft, ringA));

            InventoryTransferResult unequip = transfers.TryTransfer(equipment, backpack, helmet.Id);
            Check("Equipped item can transfer back to a collection inventory", unequip.Success &&
                backpack.TryGetItem(helmet.Id, out Item returned) && ReferenceEquals(returned, helmet) &&
                !equipment.TryGetItemAt("Head", out _));

            var grid = new GridInventoryModel(SerializableGuid.NewGuid(), "Test Grid", 4, 4, new List<Item>());
            Item gridHelmet = helmetDefinition.Create(1);
            bool gridSeeded = grid.TryAddAt(gridHelmet, 0, 0, false, out _);
            InventoryTransferResult gridPreview = transfers.PreviewToEquipmentSlot(grid, equipment, gridHelmet.Id, "Head");
            InventoryTransferResult gridEquip = transfers.TryTransferToEquipmentSlot(grid, equipment, gridHelmet.Id, "Head");
            Check("Grid item previews and equips into the specifically targeted named slot", gridSeeded && gridPreview.Success &&
                gridEquip.Success && equipment.TryGetItemAt("Head", out Item gridEquipped) && ReferenceEquals(gridEquipped, gridHelmet));

            InventoryTransferResult gridUnequip = transfers.TryTransferAt(equipment, grid, gridHelmet.Id, 1, false);
            Check("Equipped item transfers back into a targeted grid cell", gridUnequip.Success &&
                grid.TryGetItem(gridHelmet.Id, out Item gridReturned) && ReferenceEquals(gridReturned, gridHelmet) &&
                !equipment.TryGetItemAt("Head", out _));

            Debug.Log($"EQUIPMENT INVENTORY TESTS: {passed}/{passed + failed} PASSED\n{string.Join("\n", report)}", this);
            if (failed > 0) Debug.LogError("One or more equipment inventory tests failed.", this);
        }
    }
}
