using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>Applies configured effects and consumes one unit only when the effects succeed.</summary>
    [CreateAssetMenu(fileName = "Consume Item Action", menuName = "Inventory/Item Actions/Consume")]
    public sealed class InventoryItemConsumeAction : InventoryItemAction
    {
        [SerializeField] private List<InventoryConsumableEffect> effects = new List<InventoryConsumableEffect>();
        public IList<InventoryConsumableEffect> Effects => effects;

        public override InventoryItemActionAvailability GetAvailability(InventoryItemActionContext context)
        {
            if (context.Item == null || context.Item.details == null || context.Item.quantity < 1)
                return InventoryItemActionAvailability.Unavailable("There is nothing left to consume.");
            if (effects == null || effects.Count == 0)
                return InventoryItemActionAvailability.Unavailable("This consumable has no effects configured.");

            foreach (InventoryConsumableEffect effect in effects)
            {
                if (effect == null)
                    return InventoryItemActionAvailability.Unavailable("A consumable effect is missing.");
                InventoryItemActionAvailability availability = effect.GetAvailability(context);
                if (availability == null || !availability.IsAvailable)
                    return availability ?? InventoryItemActionAvailability.Unavailable("An effect could not be validated.");
            }
            return InventoryItemActionAvailability.Available();
        }

        public override InventoryItemActionResult Execute(InventoryItemActionContext context)
        {
            // Validate every effect before applying any, so ordinary requirement failures cannot
            // leave a partially applied consumable.
            InventoryItemActionAvailability availability = GetAvailability(context);
            if (!availability.IsAvailable) return InventoryItemActionResult.Failed(availability.Reason);

            foreach (InventoryConsumableEffect effect in effects)
            {
                InventoryItemActionResult effectResult = effect.Apply(context);
                if (effectResult == null || !effectResult.Success)
                    return InventoryItemActionResult.Failed(effectResult == null
                        ? "A consumable effect returned no result." : effectResult.Message);
            }

            InventoryOperationResult removal = context.Inventory.RemoveQuantityExact(context.Item.Id, 1);
            if (!removal.Success)
                return InventoryItemActionResult.Failed("The consumable could not be removed: " + removal.Failure);

            return InventoryItemActionResult.Succeeded("Consumed " + context.Item.details.Name + ".");
        }
    }
}
