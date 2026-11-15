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
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);

        //return if this hand is busy.
        int slot = CheckHandUsed(hand,ctx);
        if(ctx.handIsBusy[slot]) return;

        ctx.BroadcastAction(itemID,hand,0);
    }

    // On Release
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        // return if this hand is busy.
        int slot = CheckHandUsed(hand,ctx);
        if(ctx.handIsBusy[slot]) return;

        ctx.BroadcastAction(itemID,hand,1);
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,bool isServer,bool isClient)
    {
        if(actionTypeCode == 0)
        {
            ctx.upperBodyLayer.Play(windUps[0]);
        }

        if(actionTypeCode == 1){
            
            var state = ctx.upperBodyLayer.Play(actions[0]);
            int swingId = -1;

            int slot = CheckHandUsed(hand,ctx);
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
                };
            }

            if (!isServer) // !server because host is both client and server.
            { // we only need to fix the on end event for the player, so it will tranistion into idle
                events.OnEnd = () =>
                {
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                    ctx.handIsBusy[slot] = false;
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
