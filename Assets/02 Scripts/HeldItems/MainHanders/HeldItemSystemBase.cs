using UnityEngine;
using HSM;
using UnityEditor;

public enum SlotType {Empty, Either, RightOnly, LeftOnly, Both}
public enum HandSlot {LeftHand, RightHand, TwoHanded}

public enum HeldType {Empty, Weapon, Shield, Magic}

public enum ActionType {None, Attack, Block}
public enum ActionOrder {None, Prim, Sec, PrimSec, SecPrim}



[CreateAssetMenu(menuName = "Item System/HeldItemData")]
public class HeldItemData : ScriptableObject
{
    [Header("Basics")]
    public string heldItemName;
    public int itemID;
    public GameObject visualPrefab;
    public SlotType slotType = SlotType.Empty;
    public HeldType heldType = HeldType.Empty;

    

    //[Header("TriggerNames")]
    //public string[] primaryActionTrigger;
    //public string[] secondaryActionTrigger;

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

    //[Header("Chain Actions")]
    //public int maxPrimaryChain = 1;
    //public int maxSecondaryChain = 1;
    
}   

//[CreateAssetMenu(menuName = "Item System/Item Action")]
public abstract class ItemAction : ScriptableObject
{
    public ActionType actionType;
    public bool unStopable;
    // Called the exact frame the button is pressed down
    public abstract void OnLocalStart(HandSlot hand, PlayerContext ctx, int itemID);
    
    // Called the frame the button is released
    public abstract void OnLocalRelease(HandSlot hand, PlayerContext ctx, int itemID);
    
    // Executed on other clients via RPCs to display visuals/animations
    public abstract void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode);

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
