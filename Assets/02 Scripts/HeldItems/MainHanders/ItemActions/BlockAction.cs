using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Action/BlockAction")]
public class BlockAction : ItemAction
{
    public string actionLable;
    public string progressLable;

    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        if(!CheckIfHandsAreCorrect(hand,ctx)) return; // check to see if there is anything for the hands to do. 
        Animator anim = ctx.anim;
        //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
        string side = hand.ToString(); // we can come back to this to figure out what is the best choice.

        // Set up trigger for local anim
        string animTrigger = side + actionLable;
        string animProgress = side + progressLable;
        //Debug.Log(animTrigger);
        //Debug.Log(animProgress);

        int actionHash = ActionAnimationHasher.GetTriggerHash(animTrigger);
        int progressHash = ActionAnimationHasher.GetTriggerHash(animProgress);

        // Set variable inside of RAID.
        ctx.raid.currentActionHash = actionHash;
        ctx.raid.currentProgressHash = progressHash;

        anim.SetTrigger(actionHash);

        ctx.BroadcastAction(itemID,hand,0);
    }

    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        throw new System.NotImplementedException();
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode)
    {
        Animator anim = ctx.anim;
            //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
            // string side = hand.ToString(); // we can come back to this to figure out what is the best choice.
            //string dynamicTrigger = side + "Swing";
            
        anim.SetTrigger(ctx.raid.currentActionHash);
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        throw new System.NotImplementedException();
    }
}
