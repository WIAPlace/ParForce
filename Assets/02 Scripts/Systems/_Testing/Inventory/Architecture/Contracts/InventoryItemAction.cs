using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Reusable, data-driven action definition assigned to one or more ItemDetails assets.
    /// Implementations should be stateless: runtime state belongs in the game systems they call.
    /// </summary>
    public abstract class InventoryItemAction : ScriptableObject
    {
        [SerializeField] private string actionId;
        [SerializeField] private string displayName;
        [SerializeField] private int priority;

        public string ActionId => actionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public int Priority => priority;

        /// <summary>Return false with a user-facing reason when this action is currently unavailable.</summary>
        public abstract InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context);

        /// <summary>Execute the action. The service revalidates availability immediately before this call.</summary>
        public abstract InventoryItemActionResult Execute(InventoryItemActionContext context);

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(actionId))
                actionId = System.Guid.NewGuid().ToString("N");
        }
#endif
    }
}
