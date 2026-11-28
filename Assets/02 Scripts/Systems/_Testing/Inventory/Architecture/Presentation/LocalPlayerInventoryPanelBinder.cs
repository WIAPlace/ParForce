using System;
using Systems.Inventory.Architecture.Networking;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Scene-side bridge between the locally owned network player's inventory models
    /// and the HUD's InventoryPanelHost components. Put this on the local HUD, not on
    /// the network player prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalPlayerInventoryPanelBinder : MonoBehaviour
    {
        [Header("Local HUD Panels")]
        [SerializeField] private InventoryPanelHost inventoryPanel;
        [SerializeField] private InventoryPanelHost equipmentPanel;

        [Header("Binding Behaviour")]
        [SerializeField, Tooltip("Automatically show the inventory and equipment panels when the local player's models become ready.")]
        private bool openPanelsWhenBound = true;
        [SerializeField, Tooltip("Hide and unbind the HUD panels when the local player object despawns or this binder is disabled.")]
        private bool closePanelsWhenUnbound = true;

        private NetworkInventoryCoordinator boundCoordinator;
        private InventoryPanelSession subscribedInventorySession;
        private InventoryPanelSession subscribedEquipmentSession;

        /// <summary>The network player currently bound to this local HUD, if any.</summary>
        public NetworkInventoryCoordinator BoundCoordinator => boundCoordinator;

        /// <summary>True while this binder is connected to an initialized local player.</summary>
        public bool IsBound => boundCoordinator != null && boundCoordinator.HasLocalInventoryModels;

        /// <summary>True when at least one assigned panel is currently visible and bound.</summary>
        public bool ArePanelsOpen =>
            (inventoryPanel != null && inventoryPanel.IsOpen) ||
            (equipmentPanel != null && equipmentPanel.IsOpen);

        private void OnEnable()
        {
            NetworkInventoryCoordinator.LocalPlayerInventoryReady += HandleLocalPlayerInventoryReady;
            NetworkInventoryCoordinator.LocalPlayerInventoryUnavailable += HandleLocalPlayerInventoryUnavailable;
            TryBindExistingLocalPlayer();
        }

        private void OnDisable()
        {
            NetworkInventoryCoordinator.LocalPlayerInventoryReady -= HandleLocalPlayerInventoryReady;
            NetworkInventoryCoordinator.LocalPlayerInventoryUnavailable -= HandleLocalPlayerInventoryUnavailable;
            UnsubscribeFromDropEvents();
            Unbind();
        }

        /// <summary>Opens the assigned panels and binds them to the current local player's models.</summary>
        public void OpenPanels()
        {
            if (!TryGetBoundOwner(out PlayerInventoryOwner owner))
                return;

            try
            {
                if (inventoryPanel != null)
                    inventoryPanel.Open(owner.Inventory);

                if (equipmentPanel != null)
                    equipmentPanel.Open(owner.Equipment);

                SubscribeToDropEvents();

                if (inventoryPanel == null && equipmentPanel == null)
                    Debug.LogError($"{name}: Assign at least one InventoryPanelHost to the local player panel binder.", this);
            }
            catch (Exception exception)
            {
                inventoryPanel?.Close();
                equipmentPanel?.Close();
                Debug.LogError($"{name}: Could not bind the local player's inventory panels. Check that each InventoryPanelHost has a valid InventoryPanelView and canvas. {exception.Message}", this);
            }
        }

        /// <summary>Closes the assigned panels without destroying the player's inventory models.</summary>
        public void ClosePanels()
        {
            inventoryPanel?.Close();
            equipmentPanel?.Close();
        }

        /// <summary>Convenience method for wiring to an existing Input System action or UI button.</summary>
        public void TogglePanels()
        {
            if (ArePanelsOpen)
                ClosePanels();
            else
                OpenPanels();
        }

        /// <summary>Opens only the personal grid inventory panel.</summary>
        public void OpenInventoryPanel()
        {
            if (!TryGetBoundOwner(out PlayerInventoryOwner owner))
                return;

            if (inventoryPanel == null)
            {
                Debug.LogWarning($"{name}: No inventory panel host is assigned.", this);
                return;
            }

            inventoryPanel.Open(owner.Inventory);
            SubscribeToDropEvents();
        }

        /// <summary>Opens only the personal equipment panel.</summary>
        public void OpenEquipmentPanel()
        {
            if (!TryGetBoundOwner(out PlayerInventoryOwner owner))
                return;

            if (equipmentPanel == null)
            {
                Debug.LogWarning($"{name}: No equipment panel host is assigned.", this);
                return;
            }

            equipmentPanel.Open(owner.Equipment);
            SubscribeToDropEvents();
        }

        private void HandleLocalPlayerInventoryReady(NetworkInventoryCoordinator coordinator)
        {
            if (coordinator == null || !coordinator.HasLocalInventoryModels)
                return;

            Bind(coordinator);
        }

        private void HandleLocalPlayerInventoryUnavailable(NetworkInventoryCoordinator coordinator)
        {
            if (ReferenceEquals(boundCoordinator, coordinator))
                Unbind();
        }

        private void TryBindExistingLocalPlayer()
        {
            if (NetworkInventoryCoordinator.TryGetLocalPlayerCoordinator(out NetworkInventoryCoordinator coordinator))
                Bind(coordinator);
        }

        private void Bind(NetworkInventoryCoordinator coordinator)
        {
            if (coordinator == null || !coordinator.HasLocalInventoryModels || coordinator.InventoryOwner == null)
                return;

            if (ReferenceEquals(boundCoordinator, coordinator))
            {
                if (openPanelsWhenBound)
                    OpenPanels();
                return;
            }

            Unbind();
            boundCoordinator = coordinator;

            if (openPanelsWhenBound)
                OpenPanels();
        }

        private void SubscribeToDropEvents()
        {
            UnsubscribeFromDropEvents();

            subscribedInventorySession = inventoryPanel != null ? inventoryPanel.Session : null;
            if (subscribedInventorySession != null)
                subscribedInventorySession.ItemDropped += HandleItemDropped;

            InventoryPanelSession equipmentSession = equipmentPanel != null ? equipmentPanel.Session : null;
            if (equipmentSession != null && !ReferenceEquals(equipmentSession, subscribedInventorySession))
            {
                subscribedEquipmentSession = equipmentSession;
                subscribedEquipmentSession.ItemDropped += HandleItemDropped;
            }
        }

        private void UnsubscribeFromDropEvents()
        {
            if (subscribedInventorySession != null)
                subscribedInventorySession.ItemDropped -= HandleItemDropped;
            if (subscribedEquipmentSession != null)
                subscribedEquipmentSession.ItemDropped -= HandleItemDropped;

            subscribedInventorySession = null;
            subscribedEquipmentSession = null;
        }

        private void HandleItemDropped(Item item, InventoryPanelView sourceView)
        {
            if (item == null || sourceView == null || sourceView.CurrentModel == null)
                return;

            if (boundCoordinator == null)
            {
                InventoryOperationResult restore = sourceView.CurrentModel.Add(item);
                if (!restore.Success)
                    Debug.LogError($"{name}: The detached item could not be restored because no local network inventory coordinator is bound: {restore.Failure}.", this);
                return;
            }

            if (!boundCoordinator.TryRequestDropDetachedItem(item, sourceView.CurrentModel, out string error) &&
                !string.IsNullOrEmpty(error))
                Debug.LogWarning($"{name}: Could not request the network item drop: {error}", this);
        }

        private void Unbind()
        {
            UnsubscribeFromDropEvents();
            if (ReferenceEquals(boundCoordinator, null))
                return;

            if (closePanelsWhenUnbound)
                ClosePanels();

            boundCoordinator = null;
        }

        private bool TryGetBoundOwner(out PlayerInventoryOwner owner)
        {
            owner = null;
            if (!IsBound)
            {
                Debug.LogWarning($"{name}: The local player's inventory is not ready yet. The binder will connect automatically after NetworkInventoryCoordinator initializes it.", this);
                return false;
            }

            owner = boundCoordinator.InventoryOwner;
            return owner != null && owner.IsInitialized;
        }
    }
}
