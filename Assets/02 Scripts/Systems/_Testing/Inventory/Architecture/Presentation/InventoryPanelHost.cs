using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Scene component that creates a model and binds it to one InventoryPanelView.
    /// Add one host per simultaneously visible inventory.
    /// </summary>
    public sealed class InventoryPanelHost : MonoBehaviour
    {
        [SerializeField] private InventoryPanelView view;
        [SerializeField] private string displayName = "Inventory";
        [SerializeField] private bool useEquipmentStorage;
        [SerializeField] private EquipmentSlotLayout equipmentSlotLayout;
        [SerializeField] private bool useGridStorage;
        [SerializeField, Min(1)] private int gridWidth = 8;
        [SerializeField, Min(1)] private int gridHeight = 6;
        [SerializeField, Min(0)] private int capacity = 20;
        [SerializeField] private List<ItemDetails> startingItems = new List<ItemDetails>();
        [SerializeField] private SerializableGuid inventoryId = SerializableGuid.Empty;

        private InventoryPanelController controller;

        public IInventoryModel Model { get; private set; }

        private void OnValidate()
        {
            if (inventoryId == SerializableGuid.Empty)
                inventoryId = SerializableGuid.NewGuid();
        }

        private IEnumerator Start()
        {
            if (view == null)
            {
                Debug.LogError($"{name}: InventoryPanelView is not assigned.", this);
                yield break;
            }

            if (inventoryId == SerializableGuid.Empty)
                inventoryId = SerializableGuid.NewGuid();

            var runtimeItems = new List<Item>();
            foreach (ItemDetails details in startingItems)
            {
                if (details != null)
                    runtimeItems.Add(details.Create(1));
            }

            if (useEquipmentStorage)
            {
                if (equipmentSlotLayout == null)
                {
                    Debug.LogError($"{name}: Equipment storage is enabled, but no EquipmentSlotLayout is assigned.", this);
                    yield break;
                }

                var equipmentModel = new EquipmentInventoryModel(inventoryId, displayName, equipmentSlotLayout.Slots);
                foreach (Item item in runtimeItems)
                {
                    if (!equipmentModel.TryAdd(item, out InventoryOperationFailure failure))
                        Debug.LogWarning($"{name}: Starting item '{item.details?.name}' could not be equipped: {failure}.", this);
                }
                Model = equipmentModel;
            }
            else if (useGridStorage)
            {
                Model = new GridInventoryModel(inventoryId, displayName,
                    Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight), runtimeItems);
            }
            else
            {
                Model = new CollectionInventoryModel(inventoryId, displayName, capacity, runtimeItems);
            }

            view.Initialize();

            controller = new InventoryPanelController(view);
            controller.Bind(Model);

            yield return null;
        }

        private void OnDestroy()
        {
            controller?.Dispose();
        }
    }
}
