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
        switch (ctx.rad.type)
        {
            case ActionType.Attack:
                return ((ActionRoot)Parent).MainAttack;
            case ActionType.Block:
                return ((ActionRoot)Parent).Block;
            default:
                return base.GetTransition();
        }
    }
}
