using HSM;
using UnityEngine;
using UnityEngine.XR;

public class MainAtkLight : State
{
    readonly PlayerContext ctx;
    
    int useHand;
    int chainStep=0;
    float clipTime;
    float clipChainThreshold;
    float elapsedTime;

    public MainAtkLight(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if(elapsedTime >= clipTime)
        {
            if(ctx.rad.type == ActionType.Attack && chainStep != ctx.raid.chainLength[useHand][0])
            {
                Debug.Log("Continue");
                elapsedTime = 0;
                ctx.rad.handChainNum[useHand] += 1;
                OnEnter();
                return ((MainAttack)Parent).Light;
            }
            ctx.rad.handChainNum[useHand] = 0;
            return ((ActionRoot)Parent.Parent).Idle;
        }
        
        return base.GetTransition();
    }
    //protected override State GetInitialState() =>this;

    protected override void OnEnter()
    {
        // Debug.Log(chainStep);
        useHand = (int)ctx.rad.hand;
        ctx.rad.type = ActionType.None;

        // disable controlls for the current hand
        //ctx.rad.blockHandInput[useHand] = true;

        //int chainLength = ctx.raid.chainLength[useHand][0]; // 0 is for light attack 1 is for heavy, and so on (whatever that is)

        // chain step is current chain step. rounds back after. 
        // possibly look into cooldown after chain, with variant, so like daggers wouldnt have a long cooldown vs a hammer
        chainStep = ctx.rad.handChainNum[useHand];// = (ctx.rad.handChainNum[useHand]%chainLength)+1; // lowest it should be in this state is 1, that means its occuring.
        Debug.Log(chainStep);
        
        ctx.anim.SetInteger(ctx.raid.currentProgressHash,chainStep); // set the integer to 1 or whatever the combo is.

        clipTime = 3.33333f;//ctx.GetAnimClipLength(useHand+1); // get length of anim.
        Debug.Log("ClipTime: " + clipTime);

        // get the length of the clip 
        float chainWindowPercent = ctx.raid.chainWindow[useHand][0];
        clipChainThreshold = clipTime - (clipTime*chainWindowPercent);

        elapsedTime = 0;
    }

    protected override void OnExit()
    {
        elapsedTime = 0;
        chainStep = 0;
        ctx.rad.blockHandInput[useHand] = false;
        if (ctx.raid.unStopable[(int)ctx.rad.hand])
            ctx.rad.type = ActionType.None;
    }

    protected override void OnUpdate(float deltaTime)
    {
        elapsedTime += deltaTime;
        //Debug.Log(elapsedTime + " / " + clipTime);
        if(elapsedTime >= clipChainThreshold)
        {
            // if the hand doing the action is primary allow callbacks from the hand
            // else get it from the other main hand. LEFT OR RIGHT ONLY. if another is put in it will just revert to either left or right actions.
            // could possibly do hand.length if need be for some reason. likley not though.
            if((int)ctx.rad.hand == (int)ctx.rad.order) ctx.rad.blockHandInput[useHand]=false; 
            else ctx.rad.blockHandInput[(useHand+1)%2]=false;
        }
    }
}
