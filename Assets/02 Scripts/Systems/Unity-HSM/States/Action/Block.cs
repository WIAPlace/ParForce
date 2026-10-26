using HSM;
using UnityEngine;

public class Block : State
{
    readonly PlayerContext ctx;
    //Sheild Bash?
    

    public Block(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if(!ctx.blocking && ctx.heftTime <= 0) {
            ctx.shieldActive = false;
            return ((ActionRoot)Parent).Idle;
        }
        // Look into if we are able to cancel out of the Delay, and come back in to this state, for re raising the sheild from where it left off.
        // might just be better to have in the update loop a timer the ticks up heft time and down heft time depending on if Ctx.blocking is active, 
        // and then if heft time is 0 and ctx.blocking is false then it transitions to idle.

        return base.GetTransition();
    }

    protected override void OnEnter()
    {
        ctx.attacking = false; 
        ctx.shieldActive = false;

        ctx.heftTime = 0;
        
        Debug.Log("Block");
        base.OnEnter();
    }
    protected override void OnExit()
    {
        ctx.shieldActive = false;
        ctx.heftTime = 0;
    }
    //if hit check the direction of of ray that hit you, and then compare it to the direction looking, with an offsett on either side depending on the type of sheild.
    // if its within that range it was blocked.

    protected override void OnUpdate(float deltaTime)
    {
        // blend tree between these two for heft time.
        Hefting(deltaTime);
    }



    private void Hefting(float deltaTime)
    {
        if(!ctx.shieldActive && ctx.blocking){
            if(ctx.heftTime < ctx.shieldHeftTime)
            {
                ctx.heftTime += deltaTime;
            }
            else if (ctx.heftTime >= ctx.shieldHeftTime)
            {
                ctx.heftTime = ctx.shieldHeftTime;
                ctx.shieldActive = true;
            }
        }
        else if (!ctx.blocking)
        {
            if(ctx.shieldActive) ctx.shieldActive = false;
    
            ctx.heftTime -= deltaTime;
            
        }
    }
}
