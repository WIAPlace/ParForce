using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Runs isolated, repeatable checks against the collection model and transfer
    /// service. It creates temporary runtime models and never changes scene inventories.
    /// </summary>
    public sealed class CollectionTransferTestRunner : MonoBehaviour
    {
        [SerializeField] private ItemDetails testItemDefinition;
        [SerializeField] private ItemDetails secondTestItemDefinition;

        [ContextMenu("Run Collection Transfer Tests")]
        public void RunTests()
        {
            if (testItemDefinition == null || secondTestItemDefinition == null)
            {
                Debug.LogError("Assign two ItemDetails assets before running collection tests.", this);
                return;
            }

            var checks = new List<string>();
            int passed = 0;
            int failed = 0;

            void Check(string name, bool condition)
            {
                checks.Add($"{(condition ? "PASS" : "FAIL")}: {name}");
                if (condition) passed++;
                else failed++;
            }

            var source = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Test Source", 4);
            var destination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Test Destination", 4);
            var service = new InventoryTransferService();
            Item travellingItem = testItemDefinition.Create(1);
            Item blocker = secondTestItemDefinition.Create(1);

            source.TryAddAt(travellingItem, 0, out _);
            destination.TryAddAt(blocker, 1, out _);

            InventoryTransferResult transfer = service.TryTransferAt(
                source, destination, travellingItem.Id, 0);
            bool identityPreserved = destination.TryGetItemAt(0, out Item received) &&
                                     ReferenceEquals(received, travellingItem) && received.Id == travellingItem.Id;
            Check("Cross-collection transfer succeeds", transfer.Success);
            Check("Transferred item keeps its runtime identity", identityPreserved);
            Check("Source loses exactly one item", source.ItemCount == 0);
            Check("Destination gains exactly one item", destination.ItemCount == 2);

            Item rejectedItem = secondTestItemDefinition.Create(1);
            source.TryAddAt(rejectedItem, 2, out _);
            InventoryTransferResult occupied = service.TryTransferAt(
                source, destination, rejectedItem.Id, 1);
            Check("Occupied destination slot rejects transfer", !occupied.Success);
            Check("Rejected transfer leaves source unchanged", source.ItemCount == 1 &&
                  source.TryGetItemAt(2, out Item stillInSource) && ReferenceEquals(stillInSource, rejectedItem));
            Check("Rejected transfer leaves destination unchanged", destination.ItemCount == 2 &&
                  destination.TryGetItemAt(1, out Item stillBlocking) && ReferenceEquals(stillBlocking, blocker));

            bool localMove = source.TryMove(rejectedItem.Id, 3, out _);
            Check("Same-collection move succeeds", localMove && source.TryGetItemAt(3, out Item moved) &&
                  ReferenceEquals(moved, rejectedItem));
            int countBeforeNoOp = source.ItemCount;
            bool sameSlotMove = source.TryMove(rejectedItem.Id, 3, out _);
            Check("Moving to the same slot is a safe no-op", sameSlotMove && source.ItemCount == countBeforeNoOp);

            InventoryTransferResult sameInventory = service.TryTransfer(source, source, rejectedItem.Id);
            Check("Cross-transfer service rejects same-inventory transfer", !sameInventory.Success &&
                  sameInventory.Failure == InventoryTransferFailure.SameInventory);

            var fullDestination = new CollectionInventoryModel(SerializableGuid.NewGuid(), "Full Destination", 1,
                new[] { secondTestItemDefinition.Create(1) });
            InventoryTransferResult full = service.TryTransfer(source, fullDestination, rejectedItem.Id);
            Check("Full destination rejects transfer", !full.Success);
            Check("Full-destination rejection does not lose the item", source.TryGetItem(rejectedItem.Id, out _));

            string report = string.Join("\n", checks);
            if (failed == 0)
                Debug.Log($"COLLECTION TRANSFER TESTS: {passed}/{passed + failed} PASSED\n{report}", this);
            else
                Debug.LogError($"COLLECTION TRANSFER TESTS: {passed} PASSED, {failed} FAILED\n{report}", this);
        }
    }
}
