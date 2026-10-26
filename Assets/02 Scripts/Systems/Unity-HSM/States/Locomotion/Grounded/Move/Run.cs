using HSM;
using UnityEngine;

public class Run : State
{
    readonly PlayerContext ctx;

    public Run(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition() { 
        
        if (!ctx.grounded) {
            return ((LocomotionRoot)Parent).Airborne;
        }
        
        if(Mathf.Abs(ctx.move.magnitude) <= 0.01f)
        {
            return ((Grounded)Parent).Idle;
        }
        if (ctx.crouching)
        {
            return ((Move)Parent).MovingCrouch;
        }
        if (!ctx.sprinting)
        {
            return ((Move)Parent).Walk;
        }
        
        return null;
    }
    

    protected override void OnEnter()
    {
        if(ctx.sprinting){
            ctx.currentMoveSpeed = ctx.moveSpeed * ctx.sprintMod;
        }
    }
}
