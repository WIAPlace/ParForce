using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// A reusable, item-configurable tooltip section. Implementations should only build UI;
    /// they should not mutate inventory state.
    /// </summary>
    public abstract class InventoryItemTooltipSection : ScriptableObject
    {
        public abstract VisualElement CreateContent(InventoryItemTooltipContext context);
    }
}
