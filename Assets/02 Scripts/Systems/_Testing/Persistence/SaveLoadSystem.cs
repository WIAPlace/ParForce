using System;
using System.Collections.Generic;
using Systems.Inventory;
using Systems.Inventory.Architecture.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Systems.Persistence {
    /// <summary>
    /// Compatibility contract retained for project-side testing and other systems
    /// that still serialize identifiable data objects. The new inventory persistence
    /// implementation does not require inventory models to implement this interface.
    /// </summary>
    public interface ISaveable {
        SerializableGuid Id { get; set; }
    }

    /// <summary>
    /// Compatibility binding contract for older project-side components. It is kept
    /// available while external scripts migrate to the architecture inventory API.
    /// </summary>
    public interface IBind<TData> where TData : ISaveable {
        SerializableGuid Id { get; set; }
        void Bind(TData data);
    }

    [Serializable]
    public class GameData {
        public string Name;
        public string CurrentLevelName;
        public int SaveVersion = 2;
        // Retained to read saves created by the original single-inventory implementation.
        public LegacyInventoryData inventoryData;
        // Current architecture inventory snapshots, keyed by each model's stable inventory ID.
        public List<InventorySnapshot> inventories = new List<InventorySnapshot>();
        // Retains currency from the retired legacy inventory until a dedicated player-data model exists.
        public int LegacyCoins;
    }

    public class SaveLoadSystem : PersistentSingleton<SaveLoadSystem> {
        [SerializeField] public GameData gameData;

        IDataService dataService;

        protected override void Awake() {
            base.Awake();
            dataService = new FileDataService(new JsonSerializer());
        }

        void Start() => NewGame();

        public void NewGame() {
            InventoryPersistence.Reset();
            gameData = new GameData {
                Name = "Game",
                CurrentLevelName = "Demo",
                SaveVersion = 2,
                inventories = new List<InventorySnapshot>()
            };
            SceneManager.LoadScene(gameData.CurrentLevelName);
        }

        public void SaveGame() {
            if (gameData == null) {
                Debug.LogError("Cannot save because GameData has not been initialized.");
                return;
            }
            gameData.SaveVersion = 2;
            gameData.inventories = InventoryPersistence.CaptureAll();
            if (!InventoryPersistence.LegacyMigrationPending && gameData.inventoryData != null) {
                gameData.LegacyCoins = gameData.inventoryData.Coins;
                gameData.inventoryData = null;
            }
            dataService.Save(gameData);
        }

        public void LoadGame(string gameName) {
            gameData = dataService.Load(gameName);
            if (gameData == null) {
                Debug.LogError($"Save '{gameName}' did not contain valid game data.");
                return;
            }
            if (String.IsNullOrWhiteSpace(gameData.CurrentLevelName))
                gameData.CurrentLevelName = "Demo";
            if (gameData.inventories == null)
                gameData.inventories = new List<InventorySnapshot>();

            InventoryPersistence.BeginRestore(gameData.inventories, gameData.inventoryData);
            SceneManager.LoadScene(gameData.CurrentLevelName);
        }

        public void ReloadGame() {
            if (gameData == null || string.IsNullOrWhiteSpace(gameData.Name)) {
                Debug.LogError("Cannot reload because no current game is loaded.");
                return;
            }
            LoadGame(gameData.Name);
        }

        public void DeleteGame(string gameName) => dataService.Delete(gameName);
    }
}
