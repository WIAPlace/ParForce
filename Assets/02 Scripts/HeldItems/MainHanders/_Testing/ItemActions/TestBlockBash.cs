using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Action/TestActionBash")]
public class TestActionBash : ItemAction
{
    public ClipTransition bash;
    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        int slot = (int)hand;
        if(ctx.handIsBusy[slot]) return;
        int order = ctx.lastAction[slot].x-1;

        ctx.BroadcastAction(itemID,hand,order,0,0);
    }
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode, int actionDetails, bool isServer, bool isClient)
    {
        int slot = (int)hand;

        AnimancerState state = ctx.upperBodyLayer.Play(bash);
        var events = state.Events(ctx.sync);
        events.OnEnd = () =>
        {
            ctx.upperBodyLayer.Play(ctx.Hand[slot].idle);
            ctx.requestedAction[slot].Add(new Vector2Int(2,0),ctx.currentTick);
        };

    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        if(bash==null) return;
        PoolClip(animancer, bash.Clip, RegisteredClips);
    }

    
}
