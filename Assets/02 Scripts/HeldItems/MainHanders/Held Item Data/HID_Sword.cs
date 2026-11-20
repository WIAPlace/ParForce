using Animancer;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(menuName = "Item System/HeldItemData/Sword")]
public class HID_Sword : HeldItemData
{
    // This Item should have four Actions.
    public override int InputWhileActive(int key, bool isDown, int activeHand, Vector2Int activeAction, HeldItemData otherItem)
    {
        //Debug.Log("Entered Check " + activeAction);
        if(activeAction.x == 2 && activeAction.y==0 && key==activeHand)// if current action is secondary (blocking)
        {
            if (isDown) // Key Down
            {
                return 4; // shield bash
            }
            else return -1; // block action?
        }

        return base.InputWhileActive(key, isDown, activeHand, activeAction, otherItem);
    }
}
