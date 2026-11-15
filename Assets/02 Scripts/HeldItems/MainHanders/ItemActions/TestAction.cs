using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;


[CreateAssetMenu(menuName = "Item System/Item Action/TestAction")]
public class TestAction : ItemAction
{
    public ClipTransition[] testAnimClips;

    // On Start
    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);

        //ctx._anim.Play(testAnimClips[0]);

        ctx.BroadcastAction(itemID,hand,1);
    }

    // On Release
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        //ctx.BroadcastAction(itemID,hand,1);
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,bool isServer,bool isClient)
    {
        var state = ctx.upperBodyLayer.Play(testAnimClips[0]);

        if(actionTypeCode == 1){
            int swingId = -1;

            int slot = CheckHandUsed(hand,ctx);
            if(slot<0) return;

            // Use rayTracer as the owner key so events stay isolated per character
            var events = state.Events(ctx.sync);

            if (isServer && ctx.sync != null) // only play on server and if it is released.
            {
                events.SetCallback("_StartHit", () => swingId = ctx.sync.StartTrace(ctx.Hand[slot]));
                events.SetCallback("_EndHit", () => ctx.sync.StopTrace(swingId));
                events.OnEnd = () => {
                    ctx.sync.StopTrace(swingId);
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                };
            }

            if (!isServer) // !server because host is both client and server.
            { // we only need to fix the on end event for the player, so it will tranistion into idle
                events.OnEnd = () =>
                {
                    state.StartFade(0f,0.25f);
                    ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
                };
            }
        }
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        if(testAnimClips==null) return; // return if empty/nonexist
        
        // run through all clips in test clip and pool them to register them.
        foreach(ClipTransition clip in testAnimClips)
        {
            if(clip==null) continue;
            PoolClip(animancer, clip.Clip, RegisteredClips);
        }
    }

}
