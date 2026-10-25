using UnityEngine;
using HSM;

public class IdleCrouch : State
{
    readonly PlayerContext ctx;

    public IdleCrouch(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }
    protected override State GetTransition()
    {
        return ctx.crouching ? null : ((Idle)Parent).Stand;
    }
    protected override void OnEnter()
    {
        if(ctx.crouching)
        {
            
        }
    }
    protected override void OnExit()
    {
        
    }
}
