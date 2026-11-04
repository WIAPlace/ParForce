using HSM;
using UnityEngine;

public class BlockAction : ItemAction
{
    private static readonly int TriggerHash = Animator.StringToHash("Shield");
    private static readonly int ProgressHash = Animator.StringToHash("ShieldProgress");

    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, int itemID)
    {
        
    }

    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, int itemID)
    {
        throw new System.NotImplementedException();
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode)
    {
        throw new System.NotImplementedException();
    }

    
}
