using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Action/TestActionBlock")]
public class TestActionBlock : ItemAction
{
    public ClipTransition block;
    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        int slot = (int)hand;
        if(ctx.handIsBusy[slot]) return;
        int order = ctx.lastAction[slot].x-1;

        ctx.BroadcastAction(itemID,hand,order,0,30);
    }

    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        int slot = (int)hand;
        if(ctx.handIsBusy[slot]) return;
        int order = ctx.lastAction[slot].x-1;

        ctx.BroadcastAction(itemID,hand,order,1,30);
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode, int actionDetails, bool isServer, bool isClient)
    {
        int slot = (int)hand;
        float fadeDuration = actionDetails / 100f;
        
        if (actionTypeCode == 0)
        {
            ctx.upperBodyLayer.Play(block,fadeDuration);
        }
        else
        {
            //state.StartFade(0f,0.25f);
            ctx.upperBodyLayer.Play(ctx.Hand[slot].idle,fadeDuration);
            ctx.lastAction[slot] = Vector2Int.zero;
            
        }
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        if(block==null) return;
        PoolClip(animancer, block.Clip, RegisteredClips);
    }
}
