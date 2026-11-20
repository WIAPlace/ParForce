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
}
