using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory.Architecture.Persistence;

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

        /// <summary>The shared interaction session used by this host's panel view, once initialized.</summary>
        public InventoryPanelSession Session => view != null ? view.Session : null;

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
            //Open(CreateConfiguredModel(), defaultAppearance);
        }

        /// <summary>
        /// Dynamically displays an existing inventory model in this panel and registers it
        /// with InventoryPersistence. The host does not own or destroy the supplied model.
        /// </summary>
        public void Open(IInventoryModel model, InventoryAppearance appearance = null)
        {
            if (model == null)
                throw new System.ArgumentNullException(nameof(model));

            EnsurePanelInitialized();
            view.CancelActiveDrag();
            Model = InventoryPersistence.Register(model);
            view.SetAppearance(appearance != null ? appearance : defaultAppearance);
            view.SetPanelVisible(true);
            controller.Bind(Model);
        }
        
        /// <summary>
        /// Over load of open for easy use. will call anything that is already saved in the class.
        /// </summary>
        public void Open()
        {   // just open with the saved model.
            if(Model == null) // if no model is saved we will need to make one
            {   
                // like in start up just make one from scratch.
                Open(CreateConfiguredModel(), defaultAppearance);
            }
            else Open(Model,null);          
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

        /// <summary>Enables or disables item interaction without hiding the panel.</summary>
        public void SetInteractionEnabled(bool enabled)
        {
            view?.SetInteractionEnabled(enabled);
        }

        /// <summary>
        /// Adds an existing runtime item to the retained model, whether or not its panel is open.
        /// No UI elements are accessed; reopening the panel renders the latest model contents.
        /// </summary>
        public InventoryOperationResult AddItem(Item item)
        {
            if (Model == null)
                return InventoryOperationResult.Failed(SerializableGuid.Empty,
                    InventoryOperationKind.Add, item != null ? item.quantity : 0,
                    InventoryOperationFailure.InventoryUnavailable);
            return Model.Add(item);
        }

        /// <summary>
        /// Creates an item instance from a definition and adds it to the retained model,
        /// whether or not its panel is currently displayed. The result exposes AddedItem
        /// when the operation succeeds.
        /// </summary>
        public InventoryOperationResult AddItem(ItemDetails details, int quantity)
        {
            if (details == null || quantity < 1)
                return InventoryOperationResult.Failed(Model != null ? Model.Id : SerializableGuid.Empty,
                    InventoryOperationKind.Add, quantity,
                    details == null ? InventoryOperationFailure.InvalidItem : InventoryOperationFailure.InvalidQuantity);

            return AddItem(details.Create(quantity));
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
                    InventoryOperationResult addResult = equipmentModel.Add(item);
                    if (!addResult.Success)
                        Debug.LogWarning($"{name}: Starting item '{item.details?.name}' could not be equipped: {addResult.Failure}.", this);
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
            // Registered models intentionally outlive their panel host. This allows
            // closed/world-container inventories to remain available to the save system.
            controller?.Dispose();
        }
    }
}
