using System;
using Systems.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Server-spawned world representation of one item stack. The world object is the
    /// network identity while the item is in the shared world; its private inventory
    /// instance GUID is intentionally not sent over NGO. Each peer resolves the
    /// compact definition ID locally and instantiates that definition's visual prefab;
    /// only the world item root participates in networking.
    ///
    /// A pickup temporarily reserves the object while its owner adds the item locally.
    /// A drop is spawned reserved and becomes available only after the owner confirms
    /// that the detached item has been removed from its private inventory.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public sealed class NetworkWorldItem : NetworkBehaviour
    {
        private enum ReservationKind : byte
        {
            None,
            Pickup,
            Drop
        }

        [Header("Definition")]
        [SerializeField] private NetworkItemDefinitionCatalog definitionCatalog;
        [SerializeField, Tooltip("Optional default definition for a world item placed directly in a scene. Dynamically dropped items override this value.")]
        private ItemDetails initialDefinition;
        [SerializeField, Min(1)] private int initialQuantity = 1;

        [Header("Visual")]
        [SerializeField, Tooltip("Optional empty child used as the parent for the item-specific local visual. If unset, the NetworkWorldItem root is used.")]
        private Transform visualRoot;
        [SerializeField, Tooltip("Sprite fallback used when the ItemDetails asset has no World Visual Prefab.")]
        private SpriteRenderer iconRenderer;
        [SerializeField, Tooltip("Optional TextMesh displayed when the stack quantity is greater than one. Place it above the world visual.")]
        private TextMesh quantityLabel;
        [SerializeField, Tooltip("Prefix for the optional stack quantity label.")]
        private string quantityLabelPrefix = "x";
        [SerializeField, Tooltip("Fallback pickup collider on the NetworkWorldItem root. It is used only when the instantiated visual prefab has no usable enabled Collider components.")]
        private Collider itemCollider;

        [Header("Transaction Safety")]
        [SerializeField, Min(1f), Tooltip("How long a reserved item waits for the owning client to finish its local inventory operation.")]
        private float reservationTimeoutSeconds = 12f;

        private readonly NetworkVariable<NetworkWorldItemData> replicatedData = new NetworkVariable<NetworkWorldItemData>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> replicatedAvailable = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkItemDefinitionRegistry definitionRegistry;
        private NetworkWorldItemData pendingSpawnData;
        private bool hasPendingSpawnData;
        private ReservationKind reservationKind;
        private ulong reservationOwnerClientId;
        private uint reservationTransactionId;
        private float reservationExpiresAt;
        private NetworkObject networkObject;
        private GameObject runtimeVisualInstance;
        private GameObject runtimeVisualPrefab;
        private Collider[] runtimeVisualColliders = new Collider[0];
        private bool hasRuntimeVisualColliders;
        private Renderer quantityLabelRenderer;
        private Vector3 runtimeVisualAuthoredScale = Vector3.one;
        private bool hasWarnedAboutNetworkedVisualPrefab;

        public NetworkWorldItemData Data => replicatedData.Value;
        public bool IsPickupAvailable => IsSpawned && Data.IsValid && replicatedAvailable.Value;
        public NetworkObject NetcodeObject => networkObject != null ? networkObject : (networkObject = GetComponent<NetworkObject>());

        public event Action<NetworkWorldItemData> DataChanged;
        public event Action<bool> AvailabilityChanged;

        private void Awake()
        {
            if (networkObject == null)
                networkObject = GetComponent<NetworkObject>();
            if (itemCollider == null)
                itemCollider = GetComponent<Collider>();
            if (iconRenderer == null)
                iconRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (visualRoot == null)
                visualRoot = transform;
            if (quantityLabel != null)
            {
                quantityLabel.text = string.Empty;
                quantityLabelRenderer = quantityLabel.GetComponent<Renderer>();
                if (quantityLabelRenderer != null)
                    quantityLabelRenderer.enabled = false;
            }

            reservationTimeoutSeconds = Mathf.Max(1f, reservationTimeoutSeconds);
        }

        private void OnValidate()
        {
            initialQuantity = Mathf.Max(1, initialQuantity);
            reservationTimeoutSeconds = Mathf.Max(1f, reservationTimeoutSeconds);
            if (itemCollider == null)
                itemCollider = GetComponent<Collider>();
            if (iconRenderer == null)
                iconRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (visualRoot == null)
                visualRoot = transform;
        }

        /// <summary>
        /// Configures a dynamically spawned drop before NetworkObject.Spawn is called.
        /// The resulting world object starts reserved so another player cannot pick it
        /// up before the dropping owner confirms the local removal.
        /// </summary>
        public bool ConfigureForDropBeforeSpawn(
            ushort definitionId,
            int quantity,
            ulong ownerClientId,
            uint transactionId)
        {
            if (IsSpawned || definitionId == 0 || quantity < 1 || transactionId == 0)
                return false;

            pendingSpawnData = new NetworkWorldItemData
            {
                DefinitionId = definitionId,
                Quantity = quantity
            };
            if (!pendingSpawnData.IsValid)
                return false;

            hasPendingSpawnData = true;
            reservationKind = ReservationKind.Drop;
            reservationOwnerClientId = ownerClientId;
            reservationTransactionId = transactionId;
            return true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            replicatedData.OnValueChanged += HandleDataChanged;
            replicatedAvailable.OnValueChanged += HandleAvailabilityChanged;
            BuildDefinitionRegistry();

            if (IsServer)
            {
                NetworkWorldItemData initialData = hasPendingSpawnData
                    ? pendingSpawnData
                    : BuildInitialData();

                if (!initialData.IsValid)
                {
                    Debug.LogError($"{name}: NetworkWorldItem has no valid initial item definition or quantity. Set an initial definition or configure it before spawning.", this);
                    replicatedAvailable.Value = false;
                }
                else if (definitionRegistry != null &&
                    (!definitionRegistry.TryGetDefinition(initialData.DefinitionId, out ItemDetails definition) ||
                     initialData.Quantity > Mathf.Max(1, definition.maxStack)))
                {
                    Debug.LogError($"{name}: The world item's definition ID or quantity is invalid for the assigned Network Item Definition Catalog.", this);
                    replicatedAvailable.Value = false;
                }
                else
                {
                    replicatedData.Value = initialData;
                    bool available = reservationKind == ReservationKind.None;
                    replicatedAvailable.Value = available;
                    if (!available)
                        reservationExpiresAt = Time.unscaledTime + reservationTimeoutSeconds;
                }

                if (NetworkManager != null)
                    NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            RefreshVisuals();
        }

        public override void OnNetworkDespawn()
        {
            replicatedData.OnValueChanged -= HandleDataChanged;
            replicatedAvailable.OnValueChanged -= HandleAvailabilityChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

            ClearRuntimeVisual();
            if (quantityLabelRenderer != null)
                quantityLabelRenderer.enabled = false;
            if (iconRenderer != null)
                iconRenderer.enabled = false;
            if (itemCollider != null)
                itemCollider.enabled = false;
            SetRuntimeVisualCollidersEnabled(false);

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || reservationKind == ReservationKind.None ||
                Time.unscaledTime < reservationExpiresAt)
                return;

            // A drop that never receives confirmation must be discarded because its
            // source item may still exist in the owner's private inventory. A failed or
            // abandoned pickup is released so the world item remains obtainable.
            if (reservationKind == ReservationKind.Drop)
                DespawnReservedDrop();
            else
                ReleaseReservation();
        }

        /// <summary>Requests pickup for the locally owned player, without requiring a specific input system.</summary>
        public bool TryRequestPickup(out string error)
        {
            if (!NetworkInventoryCoordinator.TryGetLocalPlayerCoordinator(out NetworkInventoryCoordinator coordinator))
            {
                error = "The local player's inventory is not ready.";
                return false;
            }

            return coordinator.TryRequestPickup(this, out error);
        }

        internal bool TryReserveForPickup(ulong clientId, uint transactionId, out NetworkWorldItemData data)
        {
            data = Data;
            if (!IsServer || !IsSpawned || !data.IsValid || !replicatedAvailable.Value || transactionId == 0)
                return false;

            reservationKind = ReservationKind.Pickup;
            reservationOwnerClientId = clientId;
            reservationTransactionId = transactionId;
            reservationExpiresAt = Time.unscaledTime + reservationTimeoutSeconds;
            replicatedAvailable.Value = false;
            return true;
        }

        internal bool CompletePickup(ulong clientId, uint transactionId)
        {
            if (!MatchesReservation(ReservationKind.Pickup, clientId, transactionId))
                return false;

            reservationKind = ReservationKind.None;
            NetcodeObject.Despawn(true);
            return true;
        }

        internal bool CancelPickup(ulong clientId, uint transactionId)
        {
            if (!MatchesReservation(ReservationKind.Pickup, clientId, transactionId))
                return false;

            ReleaseReservation();
            return true;
        }

        internal bool CompleteDrop(ulong clientId, uint transactionId)
        {
            if (!MatchesReservation(ReservationKind.Drop, clientId, transactionId))
                return false;

            ReleaseReservation();
            return true;
        }

        internal bool CancelDrop(ulong clientId, uint transactionId)
        {
            if (!MatchesReservation(ReservationKind.Drop, clientId, transactionId))
                return false;

            DespawnReservedDrop();
            return true;
        }

        private bool MatchesReservation(ReservationKind expectedKind, ulong clientId, uint transactionId)
        {
            return IsServer && IsSpawned && reservationKind == expectedKind &&
                reservationOwnerClientId == clientId && reservationTransactionId == transactionId;
        }

        private void ReleaseReservation()
        {
            reservationKind = ReservationKind.None;
            reservationOwnerClientId = 0;
            reservationTransactionId = 0;
            reservationExpiresAt = 0f;
            if (IsSpawned && IsServer)
                replicatedAvailable.Value = true;
        }

        private void DespawnReservedDrop()
        {
            reservationKind = ReservationKind.None;
            if (IsSpawned && IsServer)
                NetcodeObject.Despawn(true);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (reservationKind == ReservationKind.None || reservationOwnerClientId != clientId)
                return;

            if (reservationKind == ReservationKind.Drop)
                DespawnReservedDrop();
            else
                ReleaseReservation();
        }

        private NetworkWorldItemData BuildInitialData()
        {
            if (initialDefinition == null || initialDefinition.NetworkDefinitionId == 0 || initialQuantity < 1)
                return default;

            return new NetworkWorldItemData
            {
                DefinitionId = initialDefinition.NetworkDefinitionId,
                Quantity = initialQuantity
            };
        }

        private void BuildDefinitionRegistry()
        {
            definitionRegistry = null;
            if (definitionCatalog == null)
                return;

            if (!definitionCatalog.TryBuildRegistry(out definitionRegistry, out System.Collections.Generic.List<string> errors))
            {
                definitionRegistry = null;
                Debug.LogError($"{name}: The Network Item Definition Catalog is invalid:\n- {string.Join("\n- ", errors)}", this);
            }
        }

        private void HandleDataChanged(NetworkWorldItemData previous, NetworkWorldItemData current)
        {
            RefreshVisuals();
            DataChanged?.Invoke(current);
        }

        private void HandleAvailabilityChanged(bool previous, bool current)
        {
            RefreshCollider();
            AvailabilityChanged?.Invoke(current);
        }

        private void RefreshVisuals()
        {
            NetworkWorldItemData data = replicatedData.Value;
            if (!data.IsValid || definitionRegistry == null ||
                !definitionRegistry.TryGetDefinition(data.DefinitionId, out ItemDetails details))
            {
                ClearRuntimeVisual();
                SetFallbackIcon(null);
                RefreshQuantityLabel(0);
                RefreshCollider();
                return;
            }

            bool spawnedPrefabVisual = TryApplyPrefabVisual(details);
            if (spawnedPrefabVisual)
            {
                // The item-specific prefab replaces, rather than layers over, the
                // generic sprite fallback on this network object.
                SetFallbackIcon(null);
            }
            else
            {
                SetFallbackIcon(details.Icon);
            }

            RefreshQuantityLabel(data.Quantity);
            RefreshCollider();
        }

        private bool TryApplyPrefabVisual(ItemDetails details)
        {
            GameObject requestedPrefab = details != null ? details.WorldVisualPrefab : null;
            if (requestedPrefab == null)
            {
                ClearRuntimeVisual();
                return false;
            }

            // Visual prefabs are instantiated locally beneath each peer's world item.
            // A NetworkObject here would create an unsupported nested network object
            // and would also duplicate the parent item's network identity.
            if (requestedPrefab.GetComponentInChildren<NetworkObject>(true) != null)
            {
                if (!hasWarnedAboutNetworkedVisualPrefab)
                {
                    Debug.LogError($"{name}: World Visual Prefab '{requestedPrefab.name}' contains a NetworkObject. World visual prefabs must be local presentation-only objects. The Sprite icon fallback will be used instead.", this);
                    hasWarnedAboutNetworkedVisualPrefab = true;
                }

                ClearRuntimeVisual();
                return false;
            }

            if (runtimeVisualInstance == null || runtimeVisualPrefab != requestedPrefab)
            {
                ClearRuntimeVisual();
                Transform parent = visualRoot != null ? visualRoot : transform;
                runtimeVisualInstance = Instantiate(requestedPrefab, parent, false);
                runtimeVisualPrefab = requestedPrefab;
                runtimeVisualAuthoredScale = runtimeVisualInstance.transform.localScale;
                ApplyWorldItemLayerToVisualHierarchy(runtimeVisualInstance);
                CacheRuntimeVisualColliders();
            }

            Transform visualTransform = runtimeVisualInstance.transform;
            visualTransform.localPosition = details.WorldVisualLocalPosition;
            visualTransform.localRotation = Quaternion.Euler(details.WorldVisualLocalEulerAngles);
            visualTransform.localScale = runtimeVisualAuthoredScale * Mathf.Max(0.01f, details.WorldVisualScale);
            return true;
        }

        /// <summary>
        /// Visual prefab children do not automatically inherit their parent's layer when
        /// instantiated. Copy the NetworkWorldItem root layer to every object in the
        /// instantiated visual hierarchy so child colliders and renderers use the same
        /// layer as the networked world item. Include inactive children as well.
        /// </summary>
        private void ApplyWorldItemLayerToVisualHierarchy(GameObject visualInstance)
        {
            if (visualInstance == null)
                return;

            int worldItemLayer = gameObject.layer;
            Transform[] hierarchy = visualInstance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < hierarchy.Length; i++)
            {
                if (hierarchy[i] != null)
                    hierarchy[i].gameObject.layer = worldItemLayer;
            }
        }

        /// <summary>
        /// Enabled Collider components authored on the visual prefab become the local
        /// pickup colliders. This supports primitive, mesh, terrain, wheel, character
        /// controller, and other Collider subclasses. The root collider remains the
        /// fallback when the visual has no usable collider.
        /// </summary>
        private void CacheRuntimeVisualColliders()
        {
            runtimeVisualColliders = new Collider[0];
            hasRuntimeVisualColliders = false;
            if (runtimeVisualInstance == null)
                return;

            Collider[] discovered = runtimeVisualInstance.GetComponentsInChildren<Collider>(true);
            if (discovered == null || discovered.Length == 0)
                return;

            // Only adopt colliders that are enabled and active in the authored visual hierarchy.
            // A MeshCollider without a mesh and a TerrainCollider without terrain data
            // cannot provide useful pickup geometry, so leave the root fallback active.
            // Trigger state and all collider-specific settings are preserved as authored.
            System.Collections.Generic.List<Collider> usable =
                new System.Collections.Generic.List<Collider>(discovered.Length);
            for (int i = 0; i < discovered.Length; i++)
            {
                Collider candidate = discovered[i];
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                    continue;

                if (candidate is MeshCollider meshCollider && meshCollider.sharedMesh == null)
                    continue;

                if (candidate is TerrainCollider terrainCollider && terrainCollider.terrainData == null)
                    continue;

                usable.Add(candidate);
            }

            if (usable.Count == 0)
                return;

            runtimeVisualColliders = usable.ToArray();
            hasRuntimeVisualColliders = runtimeVisualColliders.Length > 0;
        }

        private void SetRuntimeVisualCollidersEnabled(bool enabled)
        {
            if (runtimeVisualColliders == null)
                return;

            for (int i = 0; i < runtimeVisualColliders.Length; i++)
            {
                Collider collider = runtimeVisualColliders[i];
                if (collider != null)
                    collider.enabled = enabled;
            }
        }

        private void SetFallbackIcon(Sprite sprite)
        {
            if (iconRenderer == null)
                return;

            iconRenderer.sprite = sprite;
            iconRenderer.enabled = sprite != null;
        }

        private void RefreshQuantityLabel(int quantity)
        {
            if (quantityLabel == null)
                return;

            bool showQuantity = quantity > 1;
            quantityLabel.text = showQuantity
                ? $"{(string.IsNullOrEmpty(quantityLabelPrefix) ? string.Empty : quantityLabelPrefix)}{quantity}"
                : string.Empty;

            if (quantityLabelRenderer != null)
                quantityLabelRenderer.enabled = showQuantity;
        }

        private void ClearRuntimeVisual()
        {
            // Disable the local visual colliders immediately. Destroy() is deferred until
            // the end of the frame, so leaving them enabled could briefly leave stale
            // pickup geometry after the visual has been replaced or cleared.
            SetRuntimeVisualCollidersEnabled(false);

            if (runtimeVisualInstance != null)
                Destroy(runtimeVisualInstance);

            runtimeVisualInstance = null;
            runtimeVisualPrefab = null;
            runtimeVisualAuthoredScale = Vector3.one;
            runtimeVisualColliders = new Collider[0];
            hasRuntimeVisualColliders = false;
        }

        private void RefreshCollider()
        {
            bool pickupAvailable = IsPickupAvailable;
            if (itemCollider != null)
                itemCollider.enabled = pickupAvailable && !hasRuntimeVisualColliders;

            SetRuntimeVisualCollidersEnabled(pickupAvailable && hasRuntimeVisualColliders);
        }
    }
}
