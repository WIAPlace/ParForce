using HSM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LightTransport;

public class Block : State
{
    readonly PlayerContext ctx;
    //Sheild Bash?
    //bool isLeft;
    HandSlot useHand;
    

    public Block(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if(ctx.rad.type!=ActionType.Block && ctx.heftTime <= 0) {
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
        //ctx.attacking = false; 
        ctx.shieldActive = false;

        int hand = ctx.leftDown ? 0 : 1;// checks initaly for if we are using left or right click.

        // check wich hand is being used.
        if(ctx.rad.hand == HandSlot.TwoHanded)
        {
            if(ctx.Hand[0]!=null && ctx.Hand[0].slotType!=SlotType.Empty) useHand = HandSlot.LeftHand;
            else useHand = HandSlot.RightHand;     
        }
        else useHand = ctx.rad.hand;


        ctx.heftTime = 0;
        
        Debug.Log("Block");
        

        switch (ctx.rad.order)
        {
            case ActionOrder.Prim:
                ctx.Hand[(int)useHand].primaryAction.OnLocalStart(ctx.rad.hand, ctx, ctx.Hand[(int)useHand].itemID);
                break;

            case ActionOrder.Sec:
                ctx.Hand[(int)useHand].secondaryAction.OnLocalStart(ctx.rad.hand, ctx, ctx.Hand[(int)useHand].itemID);
                break;
            default :
                break;
        }
    }
    protected override void OnExit()
    {
        ctx.shieldActive = false;
        ctx.heftTime = 0;
        
        /*
        switch (ctx.rad.order)
        {
            case ActionOrder.Prim:
                ctx.Hand[(int)useHand].primaryAction.OnLocalRelease(ctx.rad.hand, ctx, ctx.Hand[(int)useHand].itemID);
                break;
                    
            case ActionOrder.Sec:
                ctx.Hand[(int)useHand].secondaryAction.OnLocalRelease(ctx.rad.hand, ctx, ctx.Hand[(int)useHand].itemID);
                break;
            default :
                break;
        }
        */
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
        if(!ctx.shieldActive && ctx.rad.type==ActionType.Block){
            if(ctx.heftTime < ctx.shieldHeftTime)
            {
                ctx.heftTime += deltaTime;
                ctx.anim.SetFloat(ctx.raid.currentProgressHash,ctx.heftTime);
            }
            else if (ctx.heftTime >= ctx.shieldHeftTime)
            {
                ctx.heftTime = ctx.shieldHeftTime;
                ctx.shieldActive = true;
                ctx.anim.SetFloat(ctx.raid.currentProgressHash,ctx.heftTime);
            }

        }
        else if (ctx.rad.type!=ActionType.Block)
        {
            if(ctx.shieldActive) ctx.shieldActive = false;
    
            ctx.heftTime -= deltaTime;
            
            if(ctx.heftTime <= 0) ctx.heftTime = 0;
            
            ctx.anim.SetFloat(ctx.raid.currentProgressHash,ctx.heftTime);
        }
    }
}
