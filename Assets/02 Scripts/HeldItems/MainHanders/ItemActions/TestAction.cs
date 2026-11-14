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

        ctx._anim.Play(testAnimClips[0]);

        ctx.BroadcastAction(itemID,hand,1);
    }

    // On Release
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,bool isServer,bool isClient)
    {
        var state = ctx._anim.Play(testAnimClips[0]);

        if (isServer && ctx.sync != null && actionTypeCode == 1) // only play on server and if it is released.
        {
            int swingId = -1;

            int slot = CheckHandUsed(hand,ctx);
            if(slot<0) return;

            // Use rayTracer as the owner key so events stay isolated per character
            var events = state.Events(ctx.sync);
            events.SetCallback("_StartHit", () => swingId = ctx.sync.StartTrace(ctx.Hand[slot]));
            events.SetCallback("_EndHit", () => ctx.sync.StopTrace(swingId));
            events.OnEnd = () => ctx.sync.StopTrace(swingId);
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
