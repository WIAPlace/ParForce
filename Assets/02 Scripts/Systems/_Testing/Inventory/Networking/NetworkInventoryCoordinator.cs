using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture;
using Systems.Inventory.Architecture.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// NGO bridge for one networked player. The owning peer creates and operates its
    /// private inventory models locally. Equipment is published as a compact snapshot
    /// to the server, which stores the latest public equipment state in a NetworkVariable
    /// so current and late-joining observers receive it.
    ///
    /// Private grid inventory contents are deliberately NOT replicated by this class.
    /// Shared world pickups and drops use owner requests plus server-side world-object
    /// reservations; the server does not maintain a continuously replicated private grid.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInventoryOwner))]
    public sealed class NetworkInventoryCoordinator : NetworkBehaviour
    {
        private static readonly List<NetworkInventoryCoordinator> SpawnedCoordinators =
            new List<NetworkInventoryCoordinator>();

        /// <summary>
        /// Raised on the owning peer after its local PlayerInventoryOwner has been
        /// initialized and is ready for scene-side systems such as the local HUD binder.
        /// </summary>
        public static event Action<NetworkInventoryCoordinator> LocalPlayerInventoryReady;

        /// <summary>Raised on the owning peer when its network player is despawned.</summary>
        public static event Action<NetworkInventoryCoordinator> LocalPlayerInventoryUnavailable;

        [Header("Inventory Owner")]
        [SerializeField] private PlayerInventoryOwner inventoryOwner;

        [Header("Shared Network Catalog")]
        [SerializeField] private NetworkItemDefinitionCatalog itemDefinitionCatalog;

        private NetworkVariable<NetworkEquipmentData> replicatedEquipment =
            new NetworkVariable<NetworkEquipmentData>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private NetworkItemDefinitionRegistry definitionRegistry;
        private NetworkEquipmentSlotRegistry slotRegistry;
        private NetworkItemIdRegistry itemIdRegistry;
        private readonly Dictionary<ushort, EquipmentSlotDefinition> slotDefinitionsByNetworkId =
            new Dictionary<ushort, EquipmentSlotDefinition>();

        private NetworkEquipmentItemData[] lastSubmittedEquipmentItems;
        private bool hasSubmittedEquipmentSnapshot;
        private bool subscribedToLocalEquipmentEvents;
        private bool localInventoryReadyAnnounced;
        private bool configurationValid;

        [Header("Shared World Item Transactions")]
        [SerializeField, Tooltip("Network prefab used by the server when a player drops an item. Register this prefab in the NetworkManager's Network Prefabs list.")]
        private NetworkWorldItem worldItemPrefab;
        [SerializeField, Min(0.1f)] private float pickupDistance = 3f;
        [SerializeField, Min(0.1f)] private float dropForwardDistance = 1.15f;
        [SerializeField, Min(0f)] private float dropVerticalOffset = 0.35f;

        private readonly Dictionary<uint, PendingPickup> ownerPendingPickups = new Dictionary<uint, PendingPickup>();
        private readonly HashSet<ulong> ownerPendingPickupObjectIds = new HashSet<ulong>();
        private readonly Dictionary<uint, PendingDrop> ownerPendingDrops = new Dictionary<uint, PendingDrop>();
        private readonly Dictionary<uint, NetworkWorldItem> serverPickupReservations = new Dictionary<uint, NetworkWorldItem>();
        private readonly Dictionary<uint, NetworkWorldItem> serverDropReservations = new Dictionary<uint, NetworkWorldItem>();
        private uint nextWorldTransactionId = 1;

        private enum WorldTransactionFailure : byte
        {
            None,
            InvalidRequest,
            ItemUnavailable,
            OutOfRange,
            InvalidDefinition,
            InvalidQuantity,
            InventoryRejected,
            MissingWorldItemPrefab,
            WorldItemSpawnFailed
        }

        private sealed class PendingPickup
        {
            public ulong WorldObjectId;
        }

        private sealed class PendingDrop
        {
            public Item Item;
            public IInventoryModel Source;
        }

        /// <summary>The local model owner. Initialized only on this player's owning peer.</summary>
        public PlayerInventoryOwner InventoryOwner => inventoryOwner;

        /// <summary>Latest server-published equipment snapshot, also synchronized to late joiners.</summary>
        public NetworkEquipmentData ReplicatedEquipment => replicatedEquipment.Value;

        /// <summary>Revision of the latest server-published equipment snapshot.</summary>
        public uint ReplicatedEquipmentRevision => replicatedEquipment.Value.Revision;

        /// <summary>True only on the peer that owns and initializes this player's models.</summary>
        public bool HasLocalInventoryModels => IsSpawned && IsOwner && inventoryOwner != null && inventoryOwner.IsInitialized;

        /// <summary>Raised when the public equipment snapshot changes on this peer.</summary>
        public event Action<NetworkEquipmentData, NetworkEquipmentData> ReplicatedEquipmentChanged;

        /// <summary>
        /// Finds the local player's initialized coordinator on this peer. The list is
        /// populated by NGO spawn callbacks, so callers do not need to guess spawn timing.
        /// </summary>
        public static bool TryGetLocalPlayerCoordinator(out NetworkInventoryCoordinator coordinator)
        {
            for (int i = SpawnedCoordinators.Count - 1; i >= 0; i--)
            {
                NetworkInventoryCoordinator candidate = SpawnedCoordinators[i];
                if (candidate == null)
                {
                    SpawnedCoordinators.RemoveAt(i);
                    continue;
                }

                if (candidate.HasLocalInventoryModels)
                {
                    coordinator = candidate;
                    return true;
                }
            }

            coordinator = null;
            return false;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!SpawnedCoordinators.Contains(this))
                SpawnedCoordinators.Add(this);

            configurationValid = false;

            replicatedEquipment.OnValueChanged += HandleReplicatedEquipmentChanged;

            if (!TryBuildRegistries())
                return;

            configurationValid = true;

            // Every peer has this NetworkBehaviour, but only the owning peer creates
            // the local model instances. The server still receives and republishes the
            // equipment snapshot for remote-owned player objects.
            if (!IsOwner)
            {
                if (inventoryOwner != null && inventoryOwner.IsInitialized)
                {
                    Debug.LogError($"{name}: A non-owning player replica initialized its private inventory. Keep PlayerInventoryOwner on Manual initialization, and do not initialize it outside this coordinator.", this);
                    configurationValid = false;
                }
                return;
            }

            if (inventoryOwner == null)
            {
                Debug.LogError($"{name}: NetworkInventoryCoordinator has no PlayerInventoryOwner.", this);
                configurationValid = false;
                return;
            }

            inventoryOwner.Initialize();
            if (!inventoryOwner.IsInitialized)
            {
                Debug.LogError($"{name}: The owning peer could not initialize its PlayerInventoryOwner. Check its equipment layout and inventory configuration.", this);
                configurationValid = false;
                return;
            }

            itemIdRegistry = new NetworkItemIdRegistry();
            inventoryOwner.Equipment.GameplayEventRaised += HandleLocalEquipmentGameplayEvent;
            subscribedToLocalEquipmentEvents = true;

            // Publish once on spawn so observers and late joiners have a complete
            // equipment view even when the player has not changed equipment this session.
            PublishEquipmentSnapshot();

            localInventoryReadyAnnounced = true;
            LocalPlayerInventoryReady?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (localInventoryReadyAnnounced)
            {
                localInventoryReadyAnnounced = false;
                LocalPlayerInventoryUnavailable?.Invoke(this);
            }

            SpawnedCoordinators.Remove(this);

            foreach (PendingDrop pendingDrop in ownerPendingDrops.Values)
                RestoreDetachedItem(pendingDrop.Item, pendingDrop.Source);
            ownerPendingDrops.Clear();
            ownerPendingPickups.Clear();
            ownerPendingPickupObjectIds.Clear();

            if (IsServer)
            {
                foreach (KeyValuePair<uint, NetworkWorldItem> reservation in serverPickupReservations)
                    reservation.Value?.CancelPickup(OwnerClientId, reservation.Key);
                foreach (KeyValuePair<uint, NetworkWorldItem> reservation in serverDropReservations)
                    reservation.Value?.CancelDrop(OwnerClientId, reservation.Key);
                serverPickupReservations.Clear();
                serverDropReservations.Clear();
            }

            if (subscribedToLocalEquipmentEvents && inventoryOwner != null && inventoryOwner.IsInitialized)
                inventoryOwner.Equipment.GameplayEventRaised -= HandleLocalEquipmentGameplayEvent;
            subscribedToLocalEquipmentEvents = false;

            replicatedEquipment.OnValueChanged -= HandleReplicatedEquipmentChanged;
            itemIdRegistry = null;
            lastSubmittedEquipmentItems = null;
            hasSubmittedEquipmentSnapshot = false;
            configurationValid = false;
            base.OnNetworkDespawn();
        }

        private void Awake()
        {
            if (inventoryOwner == null)
                inventoryOwner = GetComponent<PlayerInventoryOwner>();

            // This component executes before PlayerInventoryOwner.Awake, ensuring that
            // non-owning player replicas never register duplicate private inventories.
            if (inventoryOwner != null)
                inventoryOwner.PrepareForExternalInitialization();
        }

        private void OnValidate()
        {
            if (inventoryOwner == null)
                inventoryOwner = GetComponent<PlayerInventoryOwner>();

            pickupDistance = Mathf.Max(0.1f, pickupDistance);
            dropForwardDistance = Mathf.Max(0.1f, dropForwardDistance);
            dropVerticalOffset = Mathf.Max(0f, dropVerticalOffset);
        }

        public override void OnDestroy()
        {
            if (localInventoryReadyAnnounced)
            {
                localInventoryReadyAnnounced = false;
                LocalPlayerInventoryUnavailable?.Invoke(this);
            }

            SpawnedCoordinators.Remove(this);

            // NetworkBehaviour performs its own destruction cleanup. Always invoke it
            // when overriding OnDestroy, as required by Netcode for GameObjects.
            base.OnDestroy();
        }

        /// <summary>
        /// Allows a gameplay/visual system on any observer to resolve a compact equipment
        /// definition ID without receiving an ItemDetails GUID over the network.
        /// </summary>
        public bool TryResolveDefinition(ushort networkDefinitionId, out ItemDetails details)
        {
            details = null;
            return definitionRegistry != null && definitionRegistry.TryGetDefinition(networkDefinitionId, out details);
        }

        /// <summary>Resolves a compact equipment slot ID to its local authored slot key.</summary>
        public bool TryResolveSlot(ushort networkSlotId, out string slotId)
        {
            slotId = null;
            return slotRegistry != null && slotRegistry.TryGetSlotId(networkSlotId, out slotId);
        }

        /// <summary>Finds the replicated entry currently occupying a named equipment slot.</summary>
        public bool TryGetReplicatedEquipmentAt(string slotId, out NetworkEquipmentItemData item)
        {
            item = default;
            if (slotRegistry == null || replicatedEquipment.Value.Items == null ||
                !slotRegistry.TryGetNetworkSlotId(slotId, out ushort networkSlotId))
                return false;

            foreach (NetworkEquipmentItemData entry in replicatedEquipment.Value.Items)
            {
                if (entry.NetworkSlotId != networkSlotId)
                    continue;

                item = entry;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Starts a server-validated pickup. The local inventory is checked first, then
        /// the server reserves the shared world object. The owning client adds the item
        /// locally and confirms the result before the server despawns the world object.
        /// </summary>
        public bool TryRequestPickup(NetworkWorldItem worldItem, out string error)
        {
            error = null;
            if (!HasLocalInventoryModels || !configurationValid || definitionRegistry == null)
            {
                error = "The local player's inventory or network item catalog is not ready.";
                return false;
            }

            if (worldItem == null || !worldItem.IsPickupAvailable || worldItem.NetcodeObject == null)
            {
                error = "The world item is unavailable or has not finished spawning.";
                return false;
            }

            NetworkWorldItemData data = worldItem.Data;
            if (!data.IsValid || !definitionRegistry.TryGetDefinition(data.DefinitionId, out ItemDetails details))
            {
                error = "The world item references an invalid or unknown item definition.";
                return false;
            }

            Item preview = details.Create(data.Quantity);
            if (!inventoryOwner.Inventory.CanAccept(preview, out InventoryOperationFailure failure))
            {
                error = "Your inventory cannot accept this item: " + failure + ".";
                return false;
            }

            ulong worldObjectId = worldItem.NetcodeObject.NetworkObjectId;
            if (!ownerPendingPickupObjectIds.Add(worldObjectId))
            {
                error = "A pickup request for this world item is already pending.";
                return false;
            }

            uint transactionId = NextWorldTransactionId();
            ownerPendingPickups.Add(transactionId, new PendingPickup { WorldObjectId = worldObjectId });
            RequestWorldItemPickupRpc(new NetworkObjectReference(worldItem.NetcodeObject), transactionId);
            return true;
        }

        /// <summary>
        /// Receives the item already removed by InventoryPanelSession's Q-to-drop flow.
        /// If the server rejects the drop, the exact detached Item instance is restored
        /// to its source inventory. This method restores it itself when setup fails.
        /// </summary>
        public bool TryRequestDropDetachedItem(Item detachedItem, IInventoryModel source, out string error)
        {
            error = null;
            if (detachedItem == null || source == null)
            {
                error = "A detached item and its source inventory are required.";
                return false;
            }

            if (!HasLocalInventoryModels || !configurationValid || definitionRegistry == null ||
                (source.Id != inventoryOwner.Inventory.Id && source.Id != inventoryOwner.Equipment.Id))
            {
                error = "The local player's inventory is not ready, or the drop did not originate from this player.";
                RestoreDetachedItem(detachedItem, source);
                return false;
            }

            SerializableGuid definitionGuid = detachedItem.details != null
                ? detachedItem.details.Id
                : detachedItem.detailsId;
            if (!definitionRegistry.TryGetNetworkId(definitionGuid, out ushort definitionId) ||
                detachedItem.quantity < 1 ||
                (detachedItem.details != null && detachedItem.quantity > Mathf.Max(1, detachedItem.details.maxStack)))
            {
                error = "The dropped item has an invalid definition or quantity for network transmission.";
                RestoreDetachedItem(detachedItem, source);
                return false;
            }

            uint transactionId = NextWorldTransactionId();
            ownerPendingDrops.Add(transactionId, new PendingDrop
            {
                Item = detachedItem,
                Source = source
            });
            RequestWorldItemDropRpc(transactionId, definitionId, detachedItem.quantity);
            return true;
        }

        /// <summary>
        /// Convenience API for gameplay code that wants to drop an item by its local
        /// instance ID. The item is removed locally and restored automatically if the
        /// host rejects the world spawn.
        /// </summary>
        public bool TryRequestDropItem(SerializableGuid itemInstanceId, out string error)
        {
            error = null;
            if (!HasLocalInventoryModels)
            {
                error = "The local player's inventory is not ready.";
                return false;
            }

            IInventoryModel source = null;
            if (inventoryOwner.Inventory.TryGetItem(itemInstanceId, out _))
                source = inventoryOwner.Inventory;
            else if (inventoryOwner.Equipment.TryGetItem(itemInstanceId, out _))
                source = inventoryOwner.Equipment;

            if (source == null)
            {
                error = "The selected item is no longer in this player's inventory or equipment.";
                return false;
            }

            InventoryOperationResult removal = source.Remove(itemInstanceId);
            if (!removal.Success || removal.RemovedItem == null)
            {
                error = "The item could not be removed for dropping: " + removal.Failure + ".";
                return false;
            }

            return TryRequestDropDetachedItem(removal.RemovedItem, source, out error);
        }

        /// <summary>
        /// Forces a full equipment refresh from the local model. Normally this is called
        /// automatically by gameplay events; it is also useful after controlled bulk setup.
        /// </summary>
        public void PublishEquipmentSnapshot()
        {
            if (!IsSpawned || !IsOwner || !configurationValid || inventoryOwner == null ||
                !inventoryOwner.IsInitialized || definitionRegistry == null || slotRegistry == null ||
                itemIdRegistry == null)
                return;

            InventorySnapshot snapshot = InventoryPersistence.CaptureModel(inventoryOwner.Equipment);
            if (!NetworkEquipmentData.TryCreateFromSnapshot(
                    snapshot,
                    definitionRegistry,
                    itemIdRegistry,
                    slotRegistry,
                    0u,
                    out NetworkEquipmentData networkData,
                    out string error))
            {
                Debug.LogError($"{name}: Could not create a network equipment snapshot: {error}", this);
                return;
            }

            if (networkData.Items != null)
                Array.Sort(networkData.Items, CompareEquipmentEntries);

            if (hasSubmittedEquipmentSnapshot && AreEntriesEqual(lastSubmittedEquipmentItems, networkData.Items))
                return;

            hasSubmittedEquipmentSnapshot = true;
            lastSubmittedEquipmentItems = CloneEntries(networkData.Items);
            SubmitEquipmentSnapshotRpc(networkData);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestWorldItemPickupRpc(NetworkObjectReference worldItemReference, uint transactionId, RpcParams rpcParams = default)
        {
            if (!IsServer || transactionId == 0 || rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            if (serverPickupReservations.ContainsKey(transactionId))
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.InvalidRequest);
                return;
            }

            if (!worldItemReference.TryGet(out NetworkObject referencedObject) || referencedObject == null)
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.ItemUnavailable);
                return;
            }

            NetworkWorldItem worldItem = referencedObject.GetComponent<NetworkWorldItem>();
            if (worldItem == null || !worldItem.IsPickupAvailable)
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.ItemUnavailable);
                return;
            }

            float allowedDistance = Mathf.Max(0.1f, pickupDistance);
            if ((worldItem.transform.position - transform.position).sqrMagnitude > allowedDistance * allowedDistance)
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.OutOfRange);
                return;
            }

            NetworkWorldItemData data = worldItem.Data;
            if (!data.IsValid || definitionRegistry == null ||
                !definitionRegistry.TryGetDefinition(data.DefinitionId, out ItemDetails details))
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.InvalidDefinition);
                return;
            }

            if (data.Quantity > Mathf.Max(1, details.maxStack))
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.InvalidQuantity);
                return;
            }

            if (!worldItem.TryReserveForPickup(OwnerClientId, transactionId, out data))
            {
                PickupReservationResultRpc(transactionId, false, default, (byte)WorldTransactionFailure.ItemUnavailable);
                return;
            }

            serverPickupReservations.Add(transactionId, worldItem);
            PickupReservationResultRpc(transactionId, true, data, (byte)WorldTransactionFailure.None);
        }

        [Rpc(SendTo.Owner)]
        private void PickupReservationResultRpc(uint transactionId, bool accepted, NetworkWorldItemData data, byte failureCode)
        {
            if (!IsOwner)
                return;

            if (!ownerPendingPickups.TryGetValue(transactionId, out PendingPickup pending))
            {
                // If the request was cancelled locally after the server reserved the item,
                // explicitly release that reservation instead of leaving it blocked.
                if (accepted)
                    CompleteWorldItemPickupRpc(transactionId, false);
                return;
            }

            ownerPendingPickups.Remove(transactionId);
            ownerPendingPickupObjectIds.Remove(pending.WorldObjectId);

            if (!accepted)
            {
                Debug.LogWarning($"{name}: World-item pickup was rejected: {GetWorldTransactionFailureMessage(failureCode)}", this);
                return;
            }

            if (!HasLocalInventoryModels || !data.IsValid ||
                !definitionRegistry.TryGetDefinition(data.DefinitionId, out ItemDetails details))
            {
                CompleteWorldItemPickupRpc(transactionId, false);
                Debug.LogWarning($"{name}: The reserved world item could not be resolved locally. The item reservation was released.", this);
                return;
            }

            Item item = details.Create(data.Quantity);
            if (!inventoryOwner.Inventory.CanAccept(item, out InventoryOperationFailure failure))
            {
                CompleteWorldItemPickupRpc(transactionId, false);
                Debug.LogWarning($"{name}: The inventory changed before the pickup completed: {failure}.", this);
                return;
            }

            InventoryOperationResult addition = inventoryOwner.Inventory.Add(item);
            if (!addition.Success)
            {
                CompleteWorldItemPickupRpc(transactionId, false);
                Debug.LogWarning($"{name}: Could not add the reserved world item: {addition.Failure}.", this);
                return;
            }

            CompleteWorldItemPickupRpc(transactionId, true);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CompleteWorldItemPickupRpc(uint transactionId, bool pickupSucceeded, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId ||
                !serverPickupReservations.TryGetValue(transactionId, out NetworkWorldItem worldItem))
                return;

            serverPickupReservations.Remove(transactionId);
            if (worldItem == null)
                return;

            if (pickupSucceeded)
                worldItem.CompletePickup(OwnerClientId, transactionId);
            else
                worldItem.CancelPickup(OwnerClientId, transactionId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestWorldItemDropRpc(uint transactionId, ushort definitionId, int quantity, RpcParams rpcParams = default)
        {
            if (!IsServer || transactionId == 0 || rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            if (serverDropReservations.ContainsKey(transactionId))
            {
                WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.InvalidRequest);
                return;
            }

            if (worldItemPrefab == null)
            {
                WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.MissingWorldItemPrefab);
                return;
            }

            if (definitionRegistry == null || !definitionRegistry.TryGetDefinition(definitionId, out ItemDetails details))
            {
                WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.InvalidDefinition);
                return;
            }

            if (quantity < 1 || quantity > Mathf.Max(1, details.maxStack))
            {
                WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.InvalidQuantity);
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 spawnPosition = transform.position + flatForward * Mathf.Max(0.1f, dropForwardDistance) +
                Vector3.up * Mathf.Max(0f, dropVerticalOffset);
            Quaternion spawnRotation = Quaternion.LookRotation(flatForward, Vector3.up);

            NetworkWorldItem spawnedWorldItem = null;
            try
            {
                GameObject instance = Instantiate(worldItemPrefab.gameObject, spawnPosition, spawnRotation);
                spawnedWorldItem = instance.GetComponent<NetworkWorldItem>();
                NetworkObject spawnedNetworkObject = instance.GetComponent<NetworkObject>();

                if (spawnedWorldItem == null || spawnedNetworkObject == null ||
                    !spawnedWorldItem.ConfigureForDropBeforeSpawn(definitionId, quantity, OwnerClientId, transactionId))
                {
                    Destroy(instance);
                    WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.WorldItemSpawnFailed);
                    return;
                }

                spawnedNetworkObject.Spawn(true);
                if (!spawnedNetworkObject.IsSpawned)
                {
                    Destroy(instance);
                    WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.WorldItemSpawnFailed);
                    return;
                }

                serverDropReservations.Add(transactionId, spawnedWorldItem);
                WorldItemDropResultRpc(transactionId, true, (byte)WorldTransactionFailure.None);
            }
            catch (Exception exception)
            {
                if (spawnedWorldItem != null && spawnedWorldItem.IsSpawned)
                    spawnedWorldItem.CancelDrop(OwnerClientId, transactionId);
                else if (spawnedWorldItem != null)
                    Destroy(spawnedWorldItem.gameObject);

                Debug.LogError($"{name}: Could not spawn a dropped network item. Check that the prefab is registered with the NetworkManager. {exception.Message}", this);
                WorldItemDropResultRpc(transactionId, false, (byte)WorldTransactionFailure.WorldItemSpawnFailed);
            }
        }

        [Rpc(SendTo.Owner)]
        private void WorldItemDropResultRpc(uint transactionId, bool accepted, byte failureCode)
        {
            if (!IsOwner)
                return;

            if (!ownerPendingDrops.TryGetValue(transactionId, out PendingDrop pending))
            {
                if (accepted)
                    CompleteWorldItemDropRpc(transactionId, false);
                return;
            }

            ownerPendingDrops.Remove(transactionId);
            if (!accepted)
            {
                RestoreDetachedItem(pending.Item, pending.Source);
                Debug.LogWarning($"{name}: World-item drop was rejected: {GetWorldTransactionFailureMessage(failureCode)}. The item was restored.", this);
                return;
            }

            // The drop object was spawned in a reserved state. Only after the owner's
            // local source inventory has already removed the detached item do we allow
            // other players to pick up the world object.
            CompleteWorldItemDropRpc(transactionId, true);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CompleteWorldItemDropRpc(uint transactionId, bool dropSucceeded, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId ||
                !serverDropReservations.TryGetValue(transactionId, out NetworkWorldItem worldItem))
                return;

            serverDropReservations.Remove(transactionId);
            if (worldItem == null)
                return;

            if (dropSucceeded)
                worldItem.CompleteDrop(OwnerClientId, transactionId);
            else
                worldItem.CancelDrop(OwnerClientId, transactionId);
        }

        private uint NextWorldTransactionId()
        {
            uint result = nextWorldTransactionId++;
            if (result == 0)
            {
                result = nextWorldTransactionId++;
                if (result == 0)
                    result = 1;
            }
            return result;
        }

        private void RestoreDetachedItem(Item item, IInventoryModel source)
        {
            if (item == null || source == null)
                return;

            InventoryOperationResult restoration = source.Add(item);
            if (!restoration.Success)
            {
                Debug.LogError($"{name}: CRITICAL: The host rejected a world drop and the detached item could not be restored to '{source.DisplayName}': {restoration.Failure}. Manual reconciliation is required.", this);
            }
        }

        private static string GetWorldTransactionFailureMessage(byte failureCode)
        {
            switch ((WorldTransactionFailure)failureCode)
            {
                case WorldTransactionFailure.InvalidRequest: return "invalid or duplicate request";
                case WorldTransactionFailure.ItemUnavailable: return "the item is no longer available";
                case WorldTransactionFailure.OutOfRange: return "the item is too far away";
                case WorldTransactionFailure.InvalidDefinition: return "the item definition is not in the shared catalog";
                case WorldTransactionFailure.InvalidQuantity: return "the requested quantity is invalid";
                case WorldTransactionFailure.InventoryRejected: return "the inventory rejected the item";
                case WorldTransactionFailure.MissingWorldItemPrefab: return "no NetworkWorldItem prefab is assigned";
                case WorldTransactionFailure.WorldItemSpawnFailed: return "the server could not spawn the world item";
                default: return "unknown server rejection";
            }
        }

        private bool TryBuildRegistries()
        {
            if (inventoryOwner == null)
            {
                Debug.LogError($"{name}: NetworkInventoryCoordinator requires a PlayerInventoryOwner on the same GameObject.", this);
                return false;
            }

            if (itemDefinitionCatalog == null)
            {
                Debug.LogError($"{name}: Assign a shared NetworkItemDefinitionCatalog to the NetworkInventoryCoordinator.", this);
                return false;
            }

            if (!itemDefinitionCatalog.TryBuildRegistry(out definitionRegistry, out List<string> definitionErrors))
            {
                Debug.LogError($"{name}: Network item definition catalog is invalid:\n- {string.Join("\n- ", definitionErrors)}", this);
                return false;
            }

            EquipmentSlotLayout layout = inventoryOwner.ConfiguredEquipmentSlotLayout;
            if (layout == null)
            {
                Debug.LogError($"{name}: PlayerInventoryOwner requires an EquipmentSlotLayout before network spawn.", this);
                return false;
            }

            if (!NetworkEquipmentSlotRegistry.TryCreate(layout.Slots, out slotRegistry, out List<string> slotErrors))
            {
                Debug.LogError($"{name}: Network equipment slot layout is invalid:\n- {string.Join("\n- ", slotErrors)}", this);
                return false;
            }

            slotDefinitionsByNetworkId.Clear();
            foreach (EquipmentSlotDefinition slot in layout.Slots)
            {
                if (slot != null && slot.NetworkSlotId != 0)
                    slotDefinitionsByNetworkId.Add(slot.NetworkSlotId, slot);
            }

            return true;
        }

        private void HandleLocalEquipmentGameplayEvent(InventoryGameplayEvent gameplayEvent)
        {
            // The model emits gameplay events only after successful mutations. A move can
            // produce more than one event; PublishEquipmentSnapshot suppresses duplicate
            // full snapshots when the resulting equipment contents are identical.
            PublishEquipmentSnapshot();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitEquipmentSnapshotRpc(NetworkEquipmentData snapshot, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (!configurationValid || definitionRegistry == null || slotRegistry == null)
            {
                Debug.LogWarning($"{name}: Rejected equipment snapshot because this peer's network inventory configuration is invalid.", this);
                return;
            }

            // RequireOwnership is enforced by NGO. Keep an explicit sender check as a
            // second guard and to make the ownership rule visible at the application layer.
            bool serverOwnsThisPlayer = IsOwner && OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;
            if (!serverOwnsThisPlayer && rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"{name}: Rejected equipment snapshot from client {rpcParams.Receive.SenderClientId}; expected owner {OwnerClientId}.", this);
                return;
            }

            if (!TryValidateEquipmentSnapshot(snapshot, out string error))
            {
                Debug.LogWarning($"{name}: Rejected invalid equipment snapshot from owner {OwnerClientId}: {error}", this);
                return;
            }

            // Normalize empty payloads so observers always receive an empty array rather
            // than needing to distinguish an uninitialized/null list from no equipment.
            if (snapshot.Items == null)
                snapshot.Items = Array.Empty<NetworkEquipmentItemData>();

            // The server owns the public revision counter. Clients submit their latest
            // complete view; a sender cannot force a future revision and lock out later
            // legitimate updates for this player.
            snapshot.Revision = NextRevision(replicatedEquipment.Value.Revision);

            // One NetworkVariable holds the whole compact equipment snapshot. This
            // avoids emitting one NetworkList operation per slot and automatically
            // provides the current state to clients that join later.
            replicatedEquipment.Value = snapshot;
        }

        private bool TryValidateEquipmentSnapshot(NetworkEquipmentData snapshot, out string error)
        {
            if (!snapshot.IsValid(out error))
                return false;

            NetworkEquipmentItemData[] entries = snapshot.Items ?? Array.Empty<NetworkEquipmentItemData>();
            for (int i = 0; i < entries.Length; i++)
            {
                NetworkEquipmentItemData entry = entries[i];
                if (!definitionRegistry.TryGetDefinition(entry.DefinitionId, out ItemDetails details))
                {
                    error = $"Equipment entry {i} references unknown item definition ID {entry.DefinitionId}.";
                    return false;
                }

                if (details.category != ItemCategory.Equipment || entry.Quantity > Mathf.Max(1, details.maxStack))
                {
                    error = $"Equipment entry {i} has an invalid item category or quantity for '{details.Name}'.";
                    return false;
                }

                if (!slotDefinitionsByNetworkId.TryGetValue(entry.NetworkSlotId, out EquipmentSlotDefinition slot))
                {
                    error = $"Equipment entry {i} references unknown slot ID {entry.NetworkSlotId}.";
                    return false;
                }

                if (!slot.Accepts(details.equipmentType))
                {
                    error = $"Item '{details.Name}' is not accepted by equipment slot '{slot.SlotId}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void HandleReplicatedEquipmentChanged(NetworkEquipmentData previous, NetworkEquipmentData current)
        {
            ReplicatedEquipmentChanged?.Invoke(previous, current);
        }

        private static bool AreEntriesEqual(NetworkEquipmentItemData[] left, NetworkEquipmentItemData[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!left[i].Equals(right[i]))
                    return false;
            }

            return true;
        }

        private static NetworkEquipmentItemData[] CloneEntries(NetworkEquipmentItemData[] source)
        {
            if (source == null)
                return Array.Empty<NetworkEquipmentItemData>();

            var clone = new NetworkEquipmentItemData[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static int CompareEquipmentEntries(NetworkEquipmentItemData left, NetworkEquipmentItemData right)
        {
            return left.NetworkSlotId.CompareTo(right.NetworkSlotId);
        }

        private static uint NextRevision(uint current)
        {
            return current == uint.MaxValue ? 1u : current + 1u;
        }
    }
}
