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
        [SerializeField] private InventoryPanelSession session;
        [SerializeField] private string displayName = "Inventory";
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

            Model = new CollectionInventoryModel(
                inventoryId,
                displayName,
                capacity,
                runtimeItems);

            view.Initialize();

            if (session != null)
                session.Register(view);

            controller = new InventoryPanelController(view);
            controller.Bind(Model);

            yield return null;
        }

        private void OnDestroy()
        {
            controller?.Dispose();
            if (session != null && view != null)
                session.Unregister(view);
        }
    }
}
