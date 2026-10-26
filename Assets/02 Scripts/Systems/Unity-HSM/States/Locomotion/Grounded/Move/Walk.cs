using HSM;
using UnityEngine;

public class Walk : State
{
    readonly PlayerContext ctx;

    public Walk(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }
    protected override State GetTransition()
    {
        if(Mathf.Abs(ctx.move.magnitude) <= 0.01f)
        {
            return ((Grounded)Parent).Idle;
        }
        if (ctx.sprinting)
        {
            return ((Move)Parent).Run;
        }
        if (ctx.crouching)
        {
            return ((Move)Parent).MovingCrouch;
        }
        return null;
    }

    protected override void OnEnter()
    {
        ctx.currentMoveSpeed = ctx.moveSpeed;
    }
}
