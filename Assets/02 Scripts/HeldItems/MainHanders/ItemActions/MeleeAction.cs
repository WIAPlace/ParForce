using HSM;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(menuName = "Item System/Item Action/MeleeAction")]
public class MeleeAction : ItemAction
{
    public override void OnLocalStart(GameObject user, HandSlot hand, PlayerContext ctx, int itemID)
    {
        if(!CheckIfHandsAreCorrect(hand,ctx)) return; // check to see if there is anything for the hands to do. 

        Animator anim = ctx.anim;
        //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
        string side = hand.ToString(); // we can come back to this to figure out what is the best choice.

        // Set up trigger for local anim
        string animTrigger = side + "Swing";
        anim.SetTrigger(animTrigger);

        // Broadcast the exact combo index to everyone else (using it as the actionTypeCode)
        int currentComboIndex=0;
        ctx.BroadcastAction(itemID,hand,currentComboIndex);

        //PerformHitCheck(user, ctx, damageToDeal, hand);
    }

    public override void OnLocalRelease(GameObject user, HandSlot hand, PlayerContext ctx, int itemID)
    {
        //throw new System.NotImplementedException();
    }

    public override void OnRemoteExecute(GameObject user, HandSlot hand, PlayerContext ctx, int actionTypeCode)
    {
        // Remote puppets receive the raw combo index as the actionTypeCode
        int remoteComboIndex = actionTypeCode;

        // Safety check to ensure array indices match across build clients
        //if (remoteComboIndex >= 0 && remoteComboIndex < comboAnimationTriggers.Length)
        //{
            Animator anim = ctx.anim;
            //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
            string side = hand.ToString(); // we can come back to this to figure out what is the best choice.
            string dynamicTrigger = side + "Swing";
            
            anim.SetTrigger(dynamicTrigger);
        //}
    }

    /*
    private void PerformHitCheck(GameObject user, PlayerContext ctx, float damageValue, HandSlot hand)
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, attackRange))
        {
            if (hit.collider.TryGetComponent<NetworkObject>(out var netObj))
            {
                ctx.ApplyDamageServerRpc(netObj, damageValue);
            }
        }
    }
    */
}
