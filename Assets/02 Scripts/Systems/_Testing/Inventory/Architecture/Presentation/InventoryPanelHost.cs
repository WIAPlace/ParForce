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
        [SerializeField] private InventoryAppearance defaultAppearance;
        [SerializeField] private string displayName = "Inventory";
        [SerializeField] private bool useEquipmentStorage;
        [SerializeField] private EquipmentSlotLayout equipmentSlotLayout;
        [SerializeField] private bool useGridStorage;
        [SerializeField, Min(1), Tooltip("Used when no custom Grid Layout asset is assigned.")] private int gridWidth = 8;
        [SerializeField, Min(1), Tooltip("Used when no custom Grid Layout asset is assigned.")] private int gridHeight = 6;
        [SerializeField, Tooltip("Optional painted grid layout. When assigned, its dimensions and usable-cell mask replace the fallback width and height.")] private InventoryGridLayout gridLayout;
        [SerializeField, Min(0)] private int capacity = 20;
        [SerializeField] private List<ItemDetails> startingItems = new List<ItemDetails>();
        [SerializeField] private SerializableGuid inventoryId = SerializableGuid.Empty;

        private InventoryPanelController controller;

        /// <summary>
        /// The model most recently opened by this host. It remains available while the
        /// panel is closed so gameplay systems can continue adding/removing items.
        /// </summary>
        public IInventoryModel Model { get; private set; }

        /// <summary>True while this host is currently presenting its model.</summary>
        public bool IsOpen => Model != null && controller != null &&
            ReferenceEquals(controller.BoundModel, Model);

        private void OnValidate()
        {
            if (inventoryId == SerializableGuid.Empty)
                inventoryId = SerializableGuid.NewGuid();
        }

        private IEnumerator Start()
        {
            yield return null;
            if (Model != null) yield break;
            if (view == null)
            {
                Debug.LogError($"{name}: InventoryPanelView is not assigned.", this);
                yield break;
            }
            if (useEquipmentStorage && equipmentSlotLayout == null)
            {
                Debug.LogError($"{name}: Equipment storage is enabled, but no EquipmentSlotLayout is assigned.", this);
                yield break;
            }
            Open(CreateConfiguredModel(), defaultAppearance);
        }

        /// <summary>
        /// Dynamically displays an existing inventory model in this panel. The host does not
        /// own or destroy the supplied model; it only binds the panel to it.
        /// </summary>
        public void Open(IInventoryModel model, InventoryAppearance appearance = null)
        {
            if (model == null)
                throw new System.ArgumentNullException(nameof(model));

            EnsurePanelInitialized();
            view.CancelActiveDrag();
            Model = model;
            view.SetAppearance(appearance != null ? appearance : defaultAppearance);
            view.SetPanelVisible(true);
            controller.Bind(Model);
        }

        /// <summary>
        /// Unbinds and hides this panel without destroying or forgetting its inventory model.
        /// The retained Model remains usable by gameplay code while the panel is closed.
        /// </summary>
        public void Close()
        {
            if (controller != null)
                controller.Unbind();
            view?.SetPanelVisible(false);
        }

        /// <summary>
        /// Adds an existing runtime item to the retained model, whether or not its panel is open.
        /// No UI elements are accessed; reopening the panel renders the latest model contents.
        /// </summary>
        public bool TryAddItem(Item item, out InventoryOperationFailure failure)
        {
            if (Model == null)
            {
                failure = InventoryOperationFailure.InvalidItem;
                return false;
            }
            return Model.TryAdd(item, out failure);
        }

        /// <summary>
        /// Creates an item instance from a definition and adds it to the retained model,
        /// whether or not its panel is currently displayed.
        /// </summary>
        public bool TryAddItem(ItemDetails details, int quantity, out Item createdItem,
            out InventoryOperationFailure failure)
        {
            createdItem = null;
            if (details == null || quantity < 1)
            {
                failure = details == null ? InventoryOperationFailure.InvalidItem : InventoryOperationFailure.InvalidQuantity;
                return false;
            }

            Item item = details.Create(quantity);
            if (!TryAddItem(item, out failure))
                return false;

            createdItem = item;
            return true;
        }

        /// <summary>Changes the visual theme/references without changing the bound model.</summary>
        public void SetAppearance(InventoryAppearance appearance)
        {
            defaultAppearance = appearance;
            if (view != null)
                view.SetAppearance(appearance);
        }

        private void EnsurePanelInitialized()
        {
            if (view == null)
                throw new System.InvalidOperationException($"{name}: InventoryPanelView is not assigned.");
            if (controller != null) return;
            view.Initialize();
            controller = new InventoryPanelController(view);
        }

        private IInventoryModel CreateConfiguredModel()
        {
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
                var equipmentModel = new EquipmentInventoryModel(inventoryId, displayName, equipmentSlotLayout.Slots);
                foreach (Item item in runtimeItems)
                {
                    if (!equipmentModel.TryAdd(item, out InventoryOperationFailure failure))
                        Debug.LogWarning($"{name}: Starting item '{item.details?.name}' could not be equipped: {failure}.", this);
                }
                return equipmentModel;
            }

            if (useGridStorage)
            {
                if (gridLayout != null)
                    return new GridInventoryModel(inventoryId, displayName, gridLayout, runtimeItems);

                return new GridInventoryModel(inventoryId, displayName,
                    Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight), runtimeItems);
            }

            return new CollectionInventoryModel(inventoryId, displayName, capacity, runtimeItems);
        }

        private void OnDestroy()
        {
            controller?.Dispose();
        }
    }
}
