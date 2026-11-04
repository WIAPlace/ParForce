using UnityEngine;
using HSM;
using UnityEditor;

public enum SlotType {Empty, Either, RightOnly, LeftOnly, Both}

public enum HeldType {Empty, Weapon, Shield, Magic}

public enum ActionType {None, Attack, Block}

public enum HandSlot {LeftHand, RightHand, TwoHanded}

[CreateAssetMenu(menuName = "Item System/HeldItemData")]
public class HeldItemData : ScriptableObject
{
    [Header("Basics")]
    public string heldItemName;
    public int itemID;
    public GameObject visualPrefab;
    public SlotType slotType = SlotType.Empty;
    public HeldType heldType = HeldType.Empty;

    [Header("Actions")]
    public ItemAction primaryAction;
    public ItemAction secondaryAction;

    [Header("Transform Positions")]
    public string[] rayPointName; // points of your prefab that will be tracked during attack anims.

    [Header("Timer")]
    public float windUpTime;
    public float windDownTime;
    
    [Header("Data")]
    public float damage;
    public int maxChainAmnt = 1;
}   

//[CreateAssetMenu(fileName = "Item System/Item Action")]
public abstract class ItemAction : ScriptableObject
{
    public ActionType actionType;
    // Called the exact frame the button is pressed down
    public abstract void OnLocalStart(GameObject user, HandSlot hand, PlayerContext ctx, int itemID);
    
    // Called the frame the button is released
    public abstract void OnLocalRelease(GameObject user, HandSlot hand, PlayerContext ctx, int itemID);
    
    // Executed on other clients via RPCs to display visuals/animations
    public abstract void OnRemoteExecute(GameObject user, HandSlot hand, PlayerContext ctx, int actionTypeCode);

    protected bool CheckIfHandsAreCorrect(HandSlot hand, PlayerContext ctx)
    {
        switch (hand)
        {
            case HandSlot.TwoHanded when ctx.Hand[0] == null && ctx.Hand[1] == null:
                return false;
            case HandSlot.LeftHand when ctx.Hand[0] == null:
                return false;
            case HandSlot.RightHand when ctx.Hand[1] == null:
                return false;
                
            default:
                break;
        }
        return true;
    }
}
