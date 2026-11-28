using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Server-owned grid inventory attached to a shared world container such as a chest,
    /// barrel, or locker. Contents are sent as compact snapshots only when a player opens
    /// the container and when its contents change while someone is viewing it.
    ///
    /// This foundation milestone intentionally exposes read-only local mirrors. The next
    /// shared-container transaction layer will route deposits and withdrawals through
    /// server-validated requests instead of allowing local-only model mutations.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSharedInventoryContainer : NetworkBehaviour
    {
        private const byte OpenFailureNone = 0;
        private const byte OpenFailureNotInRange = 1;
        private const byte OpenFailureAtCapacity = 2;
        private const byte OpenFailureNotConfigured = 3;
        private const byte OpenFailureInvalidSnapshot = 4;

        private static readonly List<NetworkSharedInventoryContainer> SpawnedContainers =
            new List<NetworkSharedInventoryContainer>();

        /// <summary>Raised locally after a full container snapshot has been received and reconstructed.</summary>
        public static event Action<NetworkSharedInventoryContainer, IGridInventoryModel, uint> LocalContainerSnapshotReceived;

        /// <summary>Raised locally when the server closes this client's container session or the container despawns.</summary>
        public static event Action<NetworkSharedInventoryContainer> LocalContainerClosed;

        /// <summary>Raised locally when the server rejects a container open request.</summary>
        public static event Action<NetworkSharedInventoryContainer, string> LocalContainerOpenFailed;

        [Header("Container Layout")]
        [SerializeField] private string displayName = "Container";
        [SerializeField] private InventoryGridLayout gridLayout;
        [SerializeField, Min(1)] private int fallbackGridWidth = 8;
        [SerializeField, Min(1)] private int fallbackGridHeight = 6;
        [SerializeField] private List<NetworkContainerStartingItem> startingItems =
            new List<NetworkContainerStartingItem>();

        [Header("Network Configuration")]
        [SerializeField] private NetworkItemDefinitionCatalog itemDefinitionCatalog;
        [SerializeField, Min(0.1f), Tooltip("Maximum distance at which a player can open this container. The server checks this against the player's NetworkObject position.")]
        private float openDistance = 3f;
        [SerializeField, Min(0), Tooltip("Maximum simultaneous viewers. Zero means unlimited.")]
        private int maximumOpenUsers;
        [SerializeField, Tooltip("When enabled, the server closes a player's session after they move beyond 1.5 times the configured open distance.")]
        private bool closeWhenPlayerMovesAway = true;

        private readonly HashSet<ulong> openClientIds = new HashSet<ulong>();
        private readonly NetworkItemIdRegistry serverItemIds = new NetworkItemIdRegistry();
        private readonly NetworkItemIdRegistry localItemIds = new NetworkItemIdRegistry();

        private NetworkItemDefinitionRegistry definitionRegistry;
        private GridInventoryModel serverInventory;
        private GridInventoryModel localMirror;
        private NetworkInventoryData latestServerSnapshot;
        private uint serverRevision;
        private uint localRevision;
        private bool serverSnapshotDirty;
        private bool hasServerSnapshot;
        private bool localContainerOpen;
        private bool isShuttingDown;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Container" : displayName;
        public bool IsOpenLocally => localContainerOpen;
        public uint LocalRevision => localRevision;
        public int OpenUserCount => IsServer ? openClientIds.Count : 0;
        public IGridInventoryModel ServerInventory => IsServer ? serverInventory : null;
        public IGridInventoryModel LocalMirror => localMirror;

        /// <summary>Raised on the server after the authoritative container model changes.</summary>
        public event Action<NetworkSharedInventoryContainer, uint> ServerContentsChanged;

        private void OnValidate()
        {
            fallbackGridWidth = Mathf.Clamp(fallbackGridWidth, 1, 24);
            fallbackGridHeight = Mathf.Clamp(fallbackGridHeight, 1, 24);
            openDistance = Mathf.Max(0.1f, openDistance);
            maximumOpenUsers = Mathf.Max(0, maximumOpenUsers);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            isShuttingDown = false;
            if (!SpawnedContainers.Contains(this))
                SpawnedContainers.Add(this);

            if (!TryBuildDefinitionRegistry())
                return;

            if (IsServer)
            {
                if (!TryCreateServerInventory())
                    return;

                serverInventory.GameplayEventRaised += HandleServerInventoryChanged;
                if (NetworkManager != null)
                    NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            isShuttingDown = true;
            if (IsServer)
            {
                // Every peer receives the NetworkObject despawn and closes its own
                // local mirror in this callback. Avoid sending RPCs from an object
                // that is already in its despawn lifecycle.
                openClientIds.Clear();

                if (serverInventory != null)
                    serverInventory.GameplayEventRaised -= HandleServerInventoryChanged;
                if (NetworkManager != null)
                    NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                serverItemIds.Clear();
            }

            CloseLocalMirror(true);
            SpawnedContainers.Remove(this);
            serverInventory = null;
            latestServerSnapshot = default;
            hasServerSnapshot = false;
            serverSnapshotDirty = false;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            isShuttingDown = true;
            if (NetworkManager != null && IsServer)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            CloseLocalMirror();
            SpawnedContainers.Remove(this);
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsServer || isShuttingDown)
                return;

            if (closeWhenPlayerMovesAway && openClientIds.Count > 0)
                ValidateOpenClientDistances();

            if (serverSnapshotDirty && openClientIds.Count > 0)
                PublishLatestSnapshotToOpenClients();
        }

        /// <summary>Requests this container be opened by the local client.</summary>
        public bool TryRequestOpen(out string error)
        {
            error = null;
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening)
            {
                error = "The shared container is not spawned on an active network session.";
                return false;
            }

            if (IsServer)
                HandleOpenRequest(NetworkManager.ServerClientId);
            else
                RequestOpenContainerRpc();
            return true;
        }

        /// <summary>Requests the current local player's session with this container be closed.</summary>
        public bool TryRequestClose(out string error)
        {
            error = null;
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening)
            {
                CloseLocalMirror();
                error = "The shared container is no longer spawned.";
                return false;
            }

            if (IsServer)
                HandleCloseRequest(NetworkManager.ServerClientId);
            else
            {
                RequestCloseContainerRpc();
                CloseLocalMirror();
            }
            return true;
        }

        /// <summary>
        /// Server-only helper for gameplay systems to add an item stack to this container.
        /// If players currently have it open, a new compact snapshot is sent to those viewers.
        /// </summary>
        public InventoryOperationResult TryServerAddItem(ItemDetails definition, int quantity)
        {
            if (!CanMutateOnServer(InventoryOperationKind.Add, quantity, out InventoryOperationResult unavailable))
                return unavailable;
            if (definition == null || !definitionRegistry.TryGetNetworkId(definition, out _))
                return InventoryOperationResult.Failed(serverInventory.Id, InventoryOperationKind.Add,
                    quantity, InventoryOperationFailure.InvalidItem);
            if (quantity < 1 || quantity > Mathf.Max(1, definition.maxStack))
                return InventoryOperationResult.Failed(serverInventory.Id, InventoryOperationKind.Add,
                    quantity, InventoryOperationFailure.InvalidQuantity);

            return serverInventory.Add(definition.Create(quantity));
        }

        /// <summary>Server-only helper for adding a stack at a specific grid position and rotation.</summary>
        public InventoryOperationResult TryServerAddItemAt(
            ItemDetails definition, int quantity, int x, int y, int rotationQuarterTurns)
        {
            if (!CanMutateOnServer(InventoryOperationKind.AddAt, quantity, out InventoryOperationResult unavailable))
                return unavailable;
            if (definition == null || !definitionRegistry.TryGetNetworkId(definition, out _))
                return InventoryOperationResult.Failed(serverInventory.Id, InventoryOperationKind.AddAt,
                    quantity, InventoryOperationFailure.InvalidItem);
            if (quantity < 1 || quantity > Mathf.Max(1, definition.maxStack))
                return InventoryOperationResult.Failed(serverInventory.Id, InventoryOperationKind.AddAt,
                    quantity, InventoryOperationFailure.InvalidQuantity);

            return serverInventory.AddAt(definition.Create(quantity), x, y, rotationQuarterTurns);
        }

        /// <summary>Server-only helper for removing a stack by its compact container-scoped instance ID.</summary>
        public InventoryOperationResult TryServerRemoveItem(uint networkInstanceId)
        {
            if (!CanMutateOnServer(InventoryOperationKind.Remove, 0, out InventoryOperationResult unavailable))
                return unavailable;
            if (!serverItemIds.TryGetLocalId(networkInstanceId, out SerializableGuid localId))
                return InventoryOperationResult.Failed(serverInventory.Id, InventoryOperationKind.Remove,
                    0, InventoryOperationFailure.ItemNotFound);
            return serverInventory.Remove(localId);
        }

        /// <summary>Returns a compact server-side instance ID for an item in this container.</summary>
        public bool TryGetServerNetworkItemId(SerializableGuid localItemId, out uint networkInstanceId)
        {
            networkInstanceId = 0;
            if (!IsServer || serverInventory == null || localItemId == SerializableGuid.Empty)
                return false;
            EnsureServerSnapshot();
            return serverItemIds.TryGetNetworkId(localItemId, out networkInstanceId);
        }

        /// <summary>
        /// Server-side access check that can be reused by interaction systems before
        /// requesting an open. The RPC handler repeats this check authoritatively.
        /// </summary>
        public bool IsClientWithinOpenDistance(ulong clientId)
        {
            if (!IsServer || NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(
                    clientId, out NetworkClient client) ||
                client.PlayerObject == null)
            {
                Debug.LogWarning(
                    $"{name}: Could not find player object for client {clientId}.");
                return false;
            }

            Vector3 playerPosition =
                client.PlayerObject.transform.position;

            Vector3 containerPosition = transform.position;

            float distance = Vector3.Distance(
                playerPosition,
                containerPosition
            );

            Debug.Log(
                $"[CONTAINER DISTANCE] {name}\n" +
                $"Client ID: {clientId}\n" +
                $"Player Position: {playerPosition}\n" +
                $"Container Position: {containerPosition}\n" +
                $"Measured Distance: {distance:F3}m\n" +
                $"Allowed Distance: {openDistance:F3}m"
            );

            return distance <= openDistance;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestOpenContainerRpc(RpcParams rpcParams = default)
        {
            HandleOpenRequest(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCloseContainerRpc(RpcParams rpcParams = default)
        {
            HandleCloseRequest(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveContainerSnapshotRpc(bool accepted, NetworkInventoryData snapshot, byte failureCode,
            RpcParams rpcParams = default)
        {
            if (!accepted)
            {
                string failureMessage = GetOpenFailureMessage(failureCode);
                Debug.LogWarning($"{name}: The server rejected the container open request ({failureMessage}).", this);
                LocalContainerOpenFailed?.Invoke(this, failureMessage);
                return;
            }

            if (!TryBuildLocalMirror(snapshot, out GridInventoryModel newMirror, out string error))
            {
                Debug.LogError($"{name}: Could not build the local shared-container view: {error}", this);
                return;
            }

            GridInventoryModel previous = localMirror;
            localMirror = newMirror;
            localRevision = snapshot.Revision;
            localContainerOpen = true;
            LocalContainerSnapshotReceived?.Invoke(this, localMirror, localRevision);
            if (previous != null)
                InventoryPersistence.Unregister(previous);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveContainerClosedRpc(RpcParams rpcParams = default)
        {
            CloseLocalMirror();
        }

        private void HandleOpenRequest(ulong clientId)
        {
            if (!IsServer || isShuttingDown)
                return;

            if (serverInventory == null || definitionRegistry == null)
            {
                SendOpenFailure(clientId, OpenFailureNotConfigured);
                return;
            }

            if (!IsClientWithinOpenDistance(clientId))
            {
                SendOpenFailure(clientId, OpenFailureNotInRange);
                return;
            }

            if (!openClientIds.Contains(clientId) && maximumOpenUsers > 0 && openClientIds.Count >= maximumOpenUsers)
            {
                SendOpenFailure(clientId, OpenFailureAtCapacity);
                return;
            }

            openClientIds.Add(clientId);
            EnsureServerSnapshot();
            if (!hasServerSnapshot)
            {
                openClientIds.Remove(clientId);
                SendOpenFailure(clientId, OpenFailureInvalidSnapshot);
                return;
            }

            SendSnapshotToClient(clientId, latestServerSnapshot);
        }

        private void HandleCloseRequest(ulong clientId)
        {
            if (!IsServer)
                return;

            openClientIds.Remove(clientId);
            if (clientId == NetworkManager.ServerClientId)
                CloseLocalMirror();
        }

        private void SendOpenFailure(ulong clientId, byte failureCode)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
                return;
            ReceiveContainerSnapshotRpc(false, default, failureCode,
                RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void SendSnapshotToClient(ulong clientId, NetworkInventoryData snapshot)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
                return;
            ReceiveContainerSnapshotRpc(true, snapshot, OpenFailureNone,
                RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void SendClosedToClient(ulong clientId)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
                return;
            ReceiveContainerClosedRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void EnsureServerSnapshot()
        {
            if (!IsServer || serverInventory == null)
                return;
            if (hasServerSnapshot && !serverSnapshotDirty)
                return;

            InventorySnapshot inventorySnapshot = InventoryPersistence.CaptureModel(serverInventory);
            uint nextRevision = serverRevision == uint.MaxValue ? 1u : serverRevision + 1u;
            if (!NetworkInventoryData.TryCreateFromSnapshot(inventorySnapshot, definitionRegistry,
                    serverItemIds, nextRevision, out NetworkInventoryData networkData, out string error))
            {
                Debug.LogError($"{name}: Could not serialize the authoritative container inventory: {error}", this);
                hasServerSnapshot = false;
                return;
            }

            serverRevision = nextRevision;
            latestServerSnapshot = networkData;
            hasServerSnapshot = true;
            serverSnapshotDirty = false;
            ServerContentsChanged?.Invoke(this, serverRevision);
        }

        private void PublishLatestSnapshotToOpenClients()
        {
            EnsureServerSnapshot();
            if (!hasServerSnapshot)
                return;

            var viewers = new List<ulong>(openClientIds);
            foreach (ulong clientId in viewers)
                SendSnapshotToClient(clientId, latestServerSnapshot);
        }

        private bool TryBuildLocalMirror(NetworkInventoryData snapshot, out GridInventoryModel model, out string error)
        {
            model = null;
            if (definitionRegistry == null)
            {
                error = "The local item definition catalog is invalid.";
                return false;
            }
            if (!snapshot.IsValid(out error))
                return false;

            var result = CreateEmptyGrid(SerializableGuid.NewGuid());
            foreach (NetworkInventoryItemData entry in snapshot.Items ?? Array.Empty<NetworkInventoryItemData>())
            {
                if (!definitionRegistry.TryGetDefinition(entry.DefinitionId, out ItemDetails details))
                {
                    error = $"Unknown item definition ID {entry.DefinitionId}.";
                    return false;
                }
                if (!localItemIds.TryGetOrCreateLocalId(entry.InstanceId, out SerializableGuid localItemId))
                {
                    error = $"Could not resolve item instance ID {entry.InstanceId}.";
                    return false;
                }

                Item item = details.Create(entry.Quantity);
                item.Id = localItemId;
                InventoryOperationResult add = result.AddAt(item, entry.X, entry.Y, entry.RotationQuarterTurns);
                if (!add.Success)
                {
                    error = $"Item '{details.Name}' could not be placed at ({entry.X}, {entry.Y}): {add.Failure}.";
                    return false;
                }
            }

            model = result;
            error = null;
            return true;
        }

        private GridInventoryModel CreateEmptyGrid(SerializableGuid id)
        {
            return gridLayout != null
                ? new GridInventoryModel(id, DisplayName, gridLayout)
                : new GridInventoryModel(id, DisplayName,
                    Mathf.Max(1, fallbackGridWidth), Mathf.Max(1, fallbackGridHeight));
        }

        private bool TryCreateServerInventory()
        {
            serverInventory = CreateEmptyGrid(SerializableGuid.NewGuid());
            if (startingItems != null)
            {
                foreach (NetworkContainerStartingItem startingItem in startingItems)
                {
                    if (startingItem == null || startingItem.Definition == null)
                        continue;
                    if (!definitionRegistry.TryGetNetworkId(startingItem.Definition, out _))
                    {
                        Debug.LogError($"{name}: Starting item '{startingItem.Definition.name}' is missing from the assigned NetworkItemDefinitionCatalog.", this);
                        continue;
                    }
                    Item item = startingItem.Definition.Create(startingItem.Quantity);
                    InventoryOperationResult result = startingItem.UseExplicitPlacement
                        ? serverInventory.AddAt(item, startingItem.X, startingItem.Y, startingItem.RotationQuarterTurns)
                        : serverInventory.Add(item);
                    if (!result.Success)
                        Debug.LogWarning($"{name}: Starting item '{startingItem.Definition.name}' could not be added: {result.Failure}.", this);
                }
            }

            serverSnapshotDirty = true;
            return true;
        }

        private bool TryBuildDefinitionRegistry()
        {
            definitionRegistry = null;
            List<string> errors = null;
            if (itemDefinitionCatalog == null || !itemDefinitionCatalog.TryBuildRegistry(out definitionRegistry, out errors))
            {
                if (errors == null || errors.Count == 0)
                    Debug.LogError($"{name}: Assign a valid NetworkItemDefinitionCatalog to this shared container.", this);
                else
                    foreach (string error in errors)
                        Debug.LogError($"{name}: {error}", this);
                return false;
            }
            return true;
        }

        private void HandleServerInventoryChanged(InventoryGameplayEvent gameplayEvent)
        {
            if (IsServer)
                serverSnapshotDirty = true;
        }

        private void ValidateOpenClientDistances()
        {
            var clientsToClose = new List<ulong>();
            float maximumDistance = openDistance * 1.5f;
            float maximumDistanceSquared = maximumDistance * maximumDistance;
            foreach (ulong clientId in openClientIds)
            {
                if (NetworkManager == null ||
                    !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                    client.PlayerObject == null ||
                    (client.PlayerObject.transform.position - transform.position).sqrMagnitude > maximumDistanceSquared)
                    clientsToClose.Add(clientId);
            }

            foreach (ulong clientId in clientsToClose)
            {
                openClientIds.Remove(clientId);
                if (clientId == NetworkManager.ServerClientId)
                    CloseLocalMirror();
                else
                    SendClosedToClient(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            openClientIds.Remove(clientId);
        }

        private bool CanMutateOnServer(InventoryOperationKind operationKind, int quantity,
            out InventoryOperationResult failure)
        {
            if (IsSpawned && IsServer && serverInventory != null)
            {
                failure = null;
                return true;
            }

            failure = InventoryOperationResult.Failed(
                serverInventory != null ? serverInventory.Id : SerializableGuid.Empty,
                operationKind, quantity, InventoryOperationFailure.InventoryUnavailable);
            return false;
        }

        private void CloseLocalMirror(bool notifyEvenIfNotOpen = false)
        {
            bool hadLocalMirror = localContainerOpen || localMirror != null;
            if (!hadLocalMirror && !notifyEvenIfNotOpen)
                return;

            localContainerOpen = false;
            localRevision = 0;
            LocalContainerClosed?.Invoke(this);
            if (localMirror != null)
                InventoryPersistence.Unregister(localMirror);
            localMirror = null;
            localItemIds.Clear();
        }

        private static string GetOpenFailureMessage(byte failureCode)
        {
            switch (failureCode)
            {
                case OpenFailureNotInRange: return "you are too far away";
                case OpenFailureAtCapacity: return "the container is already in use by the maximum number of players";
                case OpenFailureNotConfigured: return "the server container is not initialized";
                case OpenFailureInvalidSnapshot: return "the server could not build a valid inventory snapshot";
                default: return "unknown failure";
            }
        }
    }
}
