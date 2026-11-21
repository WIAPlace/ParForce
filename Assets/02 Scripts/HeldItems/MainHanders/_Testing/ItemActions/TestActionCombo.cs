using System;
using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;


[CreateAssetMenu(menuName = "Item System/Item Action/TestActionCombo")]
public class TestActionCombo : ItemAction
{
    //[Header("Right Hand")]
    //public ClipTransition[] windUps;
    //public ClipTransition[] actions;

    [Header("Animation Clips")]
    public ClipVarient[] windUp;
    public ClipVarient[] action;


    // On Start
    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        //HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);

        //return if this hand is busy.
        int slot = (int)hand;
        if(ctx.handIsBusy[slot]) return;
        int order = ctx.lastAction[slot].x-1;

        // step through the chain. 
        int chain = ctx.chainBuffer[slot].CheckCount();
        //Debug.Log(ctx.chainBuffer[slot].CheckTick());

        ctx.BroadcastAction(itemID,hand,order,0,chain);
    }

    // On Release
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        // return if this hand is busy.
        int slot = (int)hand;
        if(ctx.handIsBusy[slot]) return;

        ctx.handIsBusy[slot] = true; // set true for user immediatly
        //Debug.Log(hand + " set to busy on tick = "+ctx.currentTick);

        int order = ctx.lastAction[slot].x-1;

        // Check wich chain step we are on
        int chain = ctx.chainBuffer[slot].CheckCount();

        ctx.BroadcastAction(itemID,hand,order,1,chain);
        
        // step through the chain after release 
        ctx.chainBuffer[slot].Step(action.Length,ctx.currentTick);
        //Debug.Log(chain);
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,int actionDetails, bool isServer,bool isClient)
    {
        if(actionTypeCode == 0)
        {
            ctx.upperBodyLayer.Play(windUp[actionDetails].hand[(int)hand],.2f);
            //Debug.Log(hand + " played wind up on tick = "+ctx.currentTick);
        }

        if(actionTypeCode == 1){
            
            var state = ctx.upperBodyLayer.Play(action[actionDetails].hand[(int)hand],.1f);
            int swingId = -1;

            int slot = (int)hand;
            if(slot<0) return;
            
            ctx.handIsBusy[slot] = true;

            // Use rayTracer as the owner key so events stay isolated per character
            var events = state.Events(ctx.sync);

            if (isServer && ctx.sync != null) // only play on server and if it is released.
            {
                swingId = ctx.sync.StartTrace(ctx.Hand[slot],slot);
                
                events.OnEnd = () => {
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                    //Debug.Log(hand + " set to free on tick = "+ ctx.currentTick);
                };
                
                ctx.exitEvents[slot].Node = state;
                ctx.exitEvents[slot].Callback = () =>
                {
                    ctx.sync.StopTrace(swingId,slot);
                    ctx.handIsBusy[slot] = false;
                    ctx.lastAction[slot] = Vector2Int.zero;
                    ctx.exitEvents[slot].Node=null;
                };
                ctx.exitEvents[slot].InvokeOnStartExiting = false;
            }

            if (!isServer) // !server because host is both client and server.
            { // we only need to fix the on end event for the player, so it will tranistion into idle
                events.OnEnd = () =>
                {
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                    //Debug.Log(hand + " set to free on tick = "+ ctx.currentTick);
                };
                ctx.exitEvents[slot].Node = state;
                ctx.exitEvents[slot].Callback = () =>
                {
                    ctx.handIsBusy[slot] = false;
                    ctx.lastAction[slot] = Vector2Int.zero;
                    ctx.exitEvents[slot].Node=null;
                };
                ctx.exitEvents[slot].InvokeOnStartExiting = false;
            }
        }
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips,int slot)
    {
        if(windUp!=null) {
            // run through all clips in test clip and pool them to register them.
            foreach(ClipVarient varient in windUp){
                foreach(ClipTransition clip in varient.hand)
                {
                    if(clip==null) continue;
                    PoolClip(animancer, clip.Clip, RegisteredClips, slot);
                }
            }
        }
        if(action!=null) {
            // run through all clips in test clip and pool them to register them.
            foreach(ClipVarient varient in action){
                foreach(ClipTransition clip in varient.hand)
                {
                    if(clip==null) continue;
                    PoolClip(animancer, clip.Clip, RegisteredClips,slot);
                }
            }
        }
    }

}
