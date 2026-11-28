using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Systems.Inventory.Architecture.Persistence;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Creates and owns the runtime inventory models belonging to one player.
    /// This component is the composition point gameplay systems can use to obtain
    /// the player's inventory and equipment interfaces without depending on UI hosts.
    ///
    /// The component intentionally has no Netcode for GameObjects dependency.
    /// A network coordinator can initialize only the owning client's models from
    /// NetworkBehaviour.OnNetworkSpawn, while ordinary projects can retain automatic
    /// initialization.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInventoryOwner : MonoBehaviour
    {
        public enum InitializationMode
        {
            /// <summary>Preserves the original behavior for non-networked owners.</summary>
            Automatic,

            /// <summary>Waits for an external bootstrapper/network coordinator to initialize this owner.</summary>
            Manual
        }

        [Header("Initialization")]
        [SerializeField, Tooltip("Use Manual for a networked player prefab. The owning client's network coordinator should initialize the owner after OnNetworkSpawn.")]
        private InitializationMode initializationMode = InitializationMode.Automatic;

        [Header("Player Inventory")]
        [SerializeField] private string inventoryDisplayName = "Player Inventory";
        [SerializeField] private InventoryPanelHost inventoryPanel;
        [SerializeField] private InventoryAppearance inventoryAppearance;
        [SerializeField] private InventoryGridLayout inventoryLayout;
        [SerializeField, Min(1)] private int fallbackGridWidth = 8;
        [SerializeField, Min(1)] private int fallbackGridHeight = 6;
        [SerializeField] private List<ItemDetails> startingInventoryItems = new List<ItemDetails>();
        [SerializeField, HideInInspector] private SerializableGuid inventoryId = SerializableGuid.Empty;

        [Header("Player Equipment")]
        [SerializeField] private string equipmentDisplayName = "Player Equipment";
        [SerializeField] private InventoryPanelHost equipmentPanel;
        [SerializeField] private InventoryAppearance equipmentAppearance;
        [SerializeField] private EquipmentSlotLayout equipmentSlotLayout;
        [SerializeField] private List<ItemDetails> startingEquipmentItems = new List<ItemDetails>();
        [SerializeField, HideInInspector] private SerializableGuid equipmentId = SerializableGuid.Empty;

        private SerializableGuid initializedPlayerIdentity = SerializableGuid.Empty;

        /// <summary>The player's primary grid inventory. Available after initialization.</summary>
        public IGridInventoryModel Inventory { get; private set; }

        /// <summary>The player's equipment inventory. Available after initialization.</summary>
        public IEquipmentInventoryModel Equipment { get; private set; }

        public bool IsInitialized => Inventory != null && Equipment != null;
        public InitializationMode CurrentInitializationMode => initializationMode;
        public SerializableGuid InventoryId => inventoryId;
        public SerializableGuid EquipmentId => equipmentId;

        /// <summary>The configured equipment layout, available before model initialization.</summary>
        public EquipmentSlotLayout ConfiguredEquipmentSlotLayout => equipmentSlotLayout;

        /// <summary>
        /// Prevents automatic Awake initialization when this owner is attached to a
        /// networked player. The NetworkBehaviour calls this from an earlier Awake,
        /// then initializes only after NGO establishes ownership in OnNetworkSpawn.
        /// </summary>
        public void PrepareForExternalInitialization()
        {
            initializationMode = InitializationMode.Manual;
        }

        private void OnValidate()
        {
            if (inventoryId == SerializableGuid.Empty)
                inventoryId = SerializableGuid.NewGuid();

            if (equipmentId == SerializableGuid.Empty)
                equipmentId = SerializableGuid.NewGuid();

            if (inventoryId == equipmentId)
                equipmentId = SerializableGuid.NewGuid();

            fallbackGridWidth = Mathf.Max(1, fallbackGridWidth);
            fallbackGridHeight = Mathf.Max(1, fallbackGridHeight);
        }

        private void Awake()
        {
            if (initializationMode == InitializationMode.Automatic)
                Initialize();
        }

        /// <summary>
        /// Creates both models once, using the serialized IDs. This preserves the
        /// original single-player/non-networked initialization path.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
                return;

            EnsureIds();
            TryInitializeModels(inventoryId, equipmentId);
        }

        /// <summary>
        /// Creates this player's models using IDs deterministically derived from a
        /// persistent player identity. Supply a stable profile/account identity, not
        /// NetworkObjectId or OwnerClientId, because NGO runtime IDs can change between sessions.
        ///
        /// Different player identities produce separate inventory and equipment IDs;
        /// the same identity produces the same IDs after reconnecting or reloading.
        /// </summary>
        public bool InitializeForPlayerIdentity(SerializableGuid playerIdentity)
        {
            if (playerIdentity == SerializableGuid.Empty)
            {
                Debug.LogError($"{name}: Cannot initialize a player inventory with an empty player identity.", this);
                return false;
            }

            SerializableGuid derivedInventoryId = DeriveModelId(playerIdentity, "Inventory");
            SerializableGuid derivedEquipmentId = DeriveModelId(playerIdentity, "Equipment");

            if (derivedInventoryId == SerializableGuid.Empty ||
                derivedEquipmentId == SerializableGuid.Empty ||
                derivedInventoryId == derivedEquipmentId)
            {
                Debug.LogError($"{name}: Could not derive distinct inventory IDs for player identity {playerIdentity}.", this);
                return false;
            }

            if (IsInitialized)
            {
                bool sameIdentity = initializedPlayerIdentity == playerIdentity;
                if (!sameIdentity)
                    Debug.LogError($"{name}: This PlayerInventoryOwner is already initialized for a different player identity. Unregister its inventories before reinitializing it.", this);
                return sameIdentity;
            }

            inventoryId = derivedInventoryId;
            equipmentId = derivedEquipmentId;
            if (!TryInitializeModels(inventoryId, equipmentId))
                return false;

            initializedPlayerIdentity = playerIdentity;
            return true;
        }

        /// <summary>
        /// Unregisters these models when this player and its inventories are permanently
        /// removed from the game. Do not call this for ordinary scene transitions or
        /// temporary network despawns when the player's inventory should be retained.
        /// </summary>
        public void UnregisterInventories()
        {
            inventoryPanel?.Close();
            equipmentPanel?.Close();

            if (Inventory != null)
                InventoryPersistence.Unregister(Inventory);

            if (Equipment != null)
                InventoryPersistence.Unregister(Equipment);

            Inventory = null;
            Equipment = null;
            initializedPlayerIdentity = SerializableGuid.Empty;
        }

        private bool TryInitializeModels(SerializableGuid requestedInventoryId, SerializableGuid requestedEquipmentId)
        {
            if (IsInitialized)
                return true;

            if (requestedInventoryId == SerializableGuid.Empty ||
                requestedEquipmentId == SerializableGuid.Empty ||
                requestedInventoryId == requestedEquipmentId)
            {
                Debug.LogError($"{name}: Player inventory and equipment require distinct, non-empty IDs.", this);
                return false;
            }

            if (equipmentSlotLayout == null)
            {
                Debug.LogError($"{name}: PlayerInventoryOwner requires an EquipmentSlotLayout.", this);
                return false;
            }

            List<Item> inventoryItems = CreateStartingItems(startingInventoryItems);
            GridInventoryModel gridModel = inventoryLayout != null
                ? new GridInventoryModel(requestedInventoryId, inventoryDisplayName, inventoryLayout, inventoryItems)
                : new GridInventoryModel(requestedInventoryId, inventoryDisplayName,
                    Mathf.Max(1, fallbackGridWidth), Mathf.Max(1, fallbackGridHeight), inventoryItems);

            WarnAboutUnplacedStartingItems(inventoryItems, gridModel);

            var equipmentModel = new EquipmentInventoryModel(
                requestedEquipmentId, equipmentDisplayName, equipmentSlotLayout.Slots);

            foreach (Item item in CreateStartingItems(startingEquipmentItems))
            {
                InventoryOperationResult result = equipmentModel.Add(item);
                if (!result.Success)
                {
                    Debug.LogWarning(
                        $"{name}: Starting equipment '{item.details?.Name}' could not be equipped: {result.Failure}.", this);
                }
            }

            // Check both IDs before registering either model. This prevents a duplicate
            // player identity from partially registering one model or consuming pending data.
            if (InventoryPersistence.IsRegistered(requestedInventoryId) ||
                InventoryPersistence.IsRegistered(requestedEquipmentId))
            {
                Debug.LogError($"{name}: One or both player inventory IDs are already registered. Each active player must have a unique identity.", this);
                return false;
            }

            // Register only after both models have been constructed. Reject any unexpected
            // collision instead of silently binding this player to another owner's model.
            IInventoryModel registeredGrid = InventoryPersistence.Register(gridModel);
            if (!ReferenceEquals(registeredGrid, gridModel) || !(registeredGrid is IGridInventoryModel gridInventory))
            {
                Debug.LogError($"{name}: Inventory ID {requestedInventoryId} is already registered to another model, or is not a grid inventory. Each player must have a unique identity.", this);
                return false;
            }

            IInventoryModel registeredEquipment = InventoryPersistence.Register(equipmentModel);
            if (!ReferenceEquals(registeredEquipment, equipmentModel) || !(registeredEquipment is IEquipmentInventoryModel equipmentInventory))
            {
                // Roll back the first registration so a failed initialization cannot leave
                // half of this player's models registered with the persistence system.
                InventoryPersistence.Unregister(gridModel);
                Debug.LogError($"{name}: Equipment ID {requestedEquipmentId} is already registered to another model, or is not an equipment inventory. Each player must have a unique identity.", this);
                return false;
            }

            Inventory = gridInventory;
            Equipment = equipmentInventory;

            inventoryPanel?.Open(Inventory, inventoryAppearance);
            equipmentPanel?.Open(Equipment, equipmentAppearance);
            Close();
            return true;
        }

        private void EnsureIds()
        {
            if (inventoryId == SerializableGuid.Empty)
                inventoryId = SerializableGuid.NewGuid();

            if (equipmentId == SerializableGuid.Empty || equipmentId == inventoryId)
                equipmentId = SerializableGuid.NewGuid();
        }

        private static SerializableGuid DeriveModelId(SerializableGuid playerIdentity, string modelRole)
        {
            byte[] identityBytes = playerIdentity.ToGuid().ToByteArray();
            byte[] roleBytes = Encoding.UTF8.GetBytes("Systems.Inventory.PlayerInventoryOwner:" + modelRole);
            byte[] input = new byte[identityBytes.Length + roleBytes.Length];
            Buffer.BlockCopy(identityBytes, 0, input, 0, identityBytes.Length);
            Buffer.BlockCopy(roleBytes, 0, input, identityBytes.Length, roleBytes.Length);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(input);
                byte[] guidBytes = new byte[16];
                Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
                return new SerializableGuid(new Guid(guidBytes));
            }
        }

        private static List<Item> CreateStartingItems(IEnumerable<ItemDetails> definitions)
        {
            var items = new List<Item>();
            if (definitions == null)
                return items;

            foreach (ItemDetails details in definitions)
            {
                if (details != null)
                    items.Add(details.Create(1));
            }

            return items;
        }

        private void WarnAboutUnplacedStartingItems(IEnumerable<Item> startingItems, IInventoryModel model)
        {
            foreach (Item item in startingItems)
            {
                if (!model.TryGetItem(item.Id, out _))
                {
                    Debug.LogWarning(
                        $"{name}: Starting inventory item '{item.details?.Name}' could not fit in the configured grid and was skipped.", this);
                }
            }
        }

        /// <summary>Opens the dedicated inventory and equipment panels.</summary>
        public void Open()
        {
            inventoryPanel?.Open();
            equipmentPanel?.Open();
        }

        /// <summary>Closes the dedicated inventory and equipment panels.</summary>
        public void Close()
        {
            inventoryPanel?.Close();
            equipmentPanel?.Close();
        }
    }
}
