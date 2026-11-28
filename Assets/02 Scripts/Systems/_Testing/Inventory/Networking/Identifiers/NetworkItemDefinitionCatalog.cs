using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory;

namespace Systems.Inventory.Architecture.Networking
{
    /// <summary>
    /// Explicit, shared list of item definitions that may be referenced by network
    /// inventory messages. Each ItemDetails asset owns its stable ushort network ID.
    /// Keep this catalog and the assigned IDs consistent across all game clients.
    /// </summary>
    [CreateAssetMenu(fileName = "Network Item Definition Catalog", menuName = "Inventory/Networking/Network Item Definition Catalog")]
    public sealed class NetworkItemDefinitionCatalog : ScriptableObject
    {
        [SerializeField, Tooltip("All item definitions that can appear in networked inventories or shared world loot.")]
        private List<ItemDetails> definitions = new List<ItemDetails>();

        public IReadOnlyList<ItemDetails> Definitions => definitions;

        public bool TryBuildRegistry(out NetworkItemDefinitionRegistry registry, out List<string> errors)
        {
            return NetworkItemDefinitionRegistry.TryCreate(definitions, out registry, out errors);
        }
    }
}
