using Animancer;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(menuName = "Item System/HeldItemData/Sword")]
public class HID_Sword : HeldItemData
{
    // This Item should have four Actions.
    public override int OffInputWhileActive(int key, bool isDown, int activeHand, Vector2Int activeAction, HeldItemData otherItem)
    {   // Active Action at the moment will always be 0,0
        return base.OffInputWhileActive(key, isDown, activeHand, activeAction, otherItem);
    }

    public override int MainInputWhileActive(bool isDown, int activeHand, Vector2Int activeAction)
    {
        if(activeAction.x == 2 && activeAction.y == 0) // if blocking
        {
            if(!isDown) return 0; // block input from this. 
            return 4;
        }
        if (activeAction.x == 4)
        {
            return 0;
        }
        return base.MainInputWhileActive(isDown, activeHand, activeAction);
    }
}
