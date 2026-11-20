using System;
using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;


[CreateAssetMenu(menuName = "Item System/Item Action/TestActionCombo")]
public class TestActionCombo : ItemAction
{
    public ClipTransition[] windUps;
    public ClipTransition[] actions;

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
        ctx.chainBuffer[slot].Step(actions.Length,ctx.currentTick);
        //Debug.Log(chain);
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,int actionDetails, bool isServer,bool isClient)
    {
        if(actionTypeCode == 0)
        {
            ctx.upperBodyLayer.Play(windUps[actionDetails],.2f);
            //Debug.Log(hand + " played wind up on tick = "+ctx.currentTick);
        }

        if(actionTypeCode == 1){
            
            var state = ctx.upperBodyLayer.Play(actions[actionDetails],.1f);
            int swingId = -1;

            int slot = (int)hand;
            if(slot<0) return;
            
            ctx.handIsBusy[slot] = true;

            // Use rayTracer as the owner key so events stay isolated per character
            var events = state.Events(ctx.sync);

            if (isServer && ctx.sync != null) // only play on server and if it is released.
            {
                swingId = ctx.sync.StartTrace(ctx.Hand[slot]);
                
                events.OnEnd = () => {
                    ctx.sync.StopTrace(swingId);
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                    ctx.handIsBusy[slot] = false;
                    //Debug.Log(hand + " set to free on tick = "+ ctx.currentTick);
                };
            }

            if (!isServer) // !server because host is both client and server.
            { // we only need to fix the on end event for the player, so it will tranistion into idle
                events.OnEnd = () =>
                {
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                    ctx.handIsBusy[slot] = false;
                    ctx.lastAction[slot] = Vector2Int.zero;
                    //Debug.Log(hand + " set to free on tick = "+ ctx.currentTick);
                };
            }
        }
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        if(windUps!=null) {
        
            // run through all clips in test clip and pool them to register them.
            foreach(ClipTransition clip in windUps)
            {
                if(clip==null) continue;
                PoolClip(animancer, clip.Clip, RegisteredClips);
            }
        }
        if(actions!=null) {
        
            // run through all clips in test clip and pool them to register them.
            foreach(ClipTransition clip in actions)
            {
                if(clip==null) continue;
                PoolClip(animancer, clip.Clip, RegisteredClips);
            }
        }
    }

}
