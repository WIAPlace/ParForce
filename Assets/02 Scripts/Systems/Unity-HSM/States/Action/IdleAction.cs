using HSM;
using UnityEngine;

public class IdleAction : State
{
    readonly PlayerContext ctx;

    public IdleAction(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if(ctx.attacking) return ((ActionRoot)Parent).MainAttack;
        if(ctx.blocking) return ((ActionRoot)Parent).Block;

        return base.GetTransition();
    }
}
