using Systems.Inventory.Architecture.Networking;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Binds a locally received shared-container snapshot to a scene-side InventoryPanelHost.
    /// Place this on the local HUD, not on the network container prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSharedInventoryContainerPanelBinder : MonoBehaviour
    {
        [SerializeField] private InventoryPanelHost containerPanel;

        private NetworkSharedInventoryContainer activeContainer;

        public NetworkSharedInventoryContainer ActiveContainer => activeContainer;
        public bool IsShowingContainer => activeContainer != null && containerPanel != null && containerPanel.IsOpen;

        private void OnEnable()
        {
            NetworkSharedInventoryContainer.LocalContainerSnapshotReceived += HandleContainerSnapshotReceived;
            NetworkSharedInventoryContainer.LocalContainerClosed += HandleContainerClosed;
            NetworkSharedInventoryContainer.LocalContainerOpenFailed += HandleContainerOpenFailed;
        }

        private void OnDisable()
        {
            NetworkSharedInventoryContainer.LocalContainerSnapshotReceived -= HandleContainerSnapshotReceived;
            NetworkSharedInventoryContainer.LocalContainerClosed -= HandleContainerClosed;
            NetworkSharedInventoryContainer.LocalContainerOpenFailed -= HandleContainerOpenFailed;
            if (activeContainer != null)
                activeContainer.TryRequestClose(out _);
            activeContainer = null;
            ClosePanelOnly();
        }

        /// <summary>
        /// Requests a container open and makes it the only container displayed by this binder.
        /// Call this from the local player's interaction/raycast code.
        /// </summary>
        public bool TryOpenContainer(NetworkSharedInventoryContainer container, out string error)
        {
            error = null;
            if (container == null)
            {
                error = "No shared inventory container was supplied.";
                return false;
            }
            if (containerPanel == null)
            {
                error = $"{name}: Assign an InventoryPanelHost for displaying shared containers.";
                Debug.LogError(error, this);
                return false;
            }

            NetworkSharedInventoryContainer previous = activeContainer;
            activeContainer = container;
            if (previous != null && !ReferenceEquals(previous, container))
                previous.TryRequestClose(out _);

            ClosePanelOnly();
            if (!container.TryRequestOpen(out error))
            {
                activeContainer = null;
                return false;
            }
            return true;
        }

        /// <summary>Closes the current panel and tells the server this player is no longer viewing the container.</summary>
        public void CloseCurrentContainer()
        {
            NetworkSharedInventoryContainer previous = activeContainer;
            activeContainer = null;
            if (previous != null)
                previous.TryRequestClose(out _);
            ClosePanelOnly();
        }

        private void HandleContainerSnapshotReceived(
            NetworkSharedInventoryContainer container,
            IGridInventoryModel model,
            uint revision)
        {
            if (container == null || model == null)
                return;

            // Only display snapshots for the container the local player explicitly
            // requested through this binder. This also ignores late snapshots from a
            // previously viewed container after the player has switched targets.
            if (!ReferenceEquals(activeContainer, container))
            {
                container.TryRequestClose(out _);
                return;
            }

            try
            {
                containerPanel.Open(model);
                // Until the server-mediated transfer milestone is added, this panel is
                // intentionally view-only so a client cannot mutate a local mirror.
                containerPanel.SetInteractionEnabled(false);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"{name}: Could not open the shared container panel. Check its InventoryPanelView and canvas references. {exception.Message}", this);
                containerPanel.Close();
                activeContainer = null;
                container.TryRequestClose(out _);
            }
        }

        private void HandleContainerClosed(NetworkSharedInventoryContainer container)
        {
            if (!ReferenceEquals(activeContainer, container))
                return;

            activeContainer = null;
            ClosePanelOnly();
        }

        private void HandleContainerOpenFailed(NetworkSharedInventoryContainer container, string reason)
        {
            if (!ReferenceEquals(activeContainer, container))
                return;

            activeContainer = null;
            ClosePanelOnly();
            Debug.LogWarning($"{name}: Could not open '{container.DisplayName}': {reason}.", this);
        }

        private void ClosePanelOnly()
        {
            if (containerPanel == null)
                return;
            containerPanel.Close();
            containerPanel.SetInteractionEnabled(true);
        }
    }
}
