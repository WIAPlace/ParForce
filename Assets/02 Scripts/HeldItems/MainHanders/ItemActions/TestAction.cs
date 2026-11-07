using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;

[CreateAssetMenu(menuName = "Item System/Item Action/TestAction")]
public class TestAction : ItemAction
{
    public AnimationClip[] testAnimClips;

    // On Start
    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);

        ctx._anim.Play(testAnimClips[0]);

        ctx.BroadcastAction(itemID,hand,0);
    }

    // On Release
    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode)
    {
        ctx._anim.Play(testAnimClips[0]);
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        if(testAnimClips==null) return; // return if empty/nonexist
        
        // run through all clips in test clip and pool them to register them.
        foreach(AnimationClip clip in testAnimClips)
        {
            if(clip==null) continue;
            PoolClip(animancer, clip, RegisteredClips);
        }
    }

}
