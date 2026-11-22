using Systems.Inventory;
using UnityEngine;

// will be used for transfering items between containers.
public static class ContainerServices
{
    public static void TryMoveItem(Slot sourceSlot, Slot targetSlot)
    {
        InventoryModel sourceModel = sourceSlot.controlRef.model;
        InventoryModel targetModel = targetSlot.controlRef.model;

        int targetIndex = targetSlot.Index;
        int sourceIndex = sourceSlot.Index;

        // if Other model's slot has nothing in it.
        if(targetModel.Get(targetIndex).detailsId == SerializableGuid.Empty)
        {
            //Remove item from sourse Model and add it to target model at target index.
            if (targetModel.AddAt(targetIndex, sourceModel.Get(sourceIndex)))
            {
                sourceModel.RemoveAt(sourceIndex);
            }
            // if adding to the other one fails, just place it back.
            else sourceModel.AddAt(targetIndex, sourceModel.Get(sourceIndex));
            return;
        }
        var sourceItemId = sourceModel.Get(sourceIndex).details.Id;
        var targetItemId = targetModel.Get(targetIndex).details.Id;


    }
}
