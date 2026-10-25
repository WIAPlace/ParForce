using HSM;
using UnityEngine;

public class Stand : State
{
    readonly PlayerContext ctx;

    public Stand(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        return !ctx.crouching ? null : ((Idle)Parent).IdelCrouch;
    }
}
