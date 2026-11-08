using UnityEngine;
using HSM;

public class MainAttack : State {
    readonly PlayerContext ctx;
    public readonly MainAtkWindUp WindUp;
    public readonly MainAtkLight Light;
    public readonly MainAtkHeavy Heavy;

    int useHand;

    public MainAttack(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
        WindUp = new MainAtkWindUp(m, this, ctx);
        Light = new MainAtkLight(m, this, ctx);
        Heavy = new MainAtkHeavy(m, this, ctx);
    }

    protected override State GetInitialState()=>WindUp;

    protected override State GetTransition()
    {
        return base.GetTransition();
    }

    protected override void OnEnter()
    {
        useHand = (int)ctx.rad.hand; // get which hand is being used

        ctx.rad.handChainNum[useHand] = 0; // set the chain count to 0 to start off.

        // activate the hands item SO to get datta.
        ctx.Hand[useHand].primaryAction.OnLocalStart(ctx.rad.hand, ctx, ctx.Hand[useHand].id);

        ctx.anim.SetBool(ctx.raid.currentActionHash,true); // set us to playing the anim.
    }

    protected override void OnExit()
    {   
        //Debug.Log("ExitMain");
        useHand = (int)ctx.rad.hand; // get which hand is being used
        ctx.rad.handChainNum[useHand] = 0; // set the chain count to 0 to end.
        ctx.raid.chainLength[useHand] = new int[0]; // reset this hand's chain length.

        ctx.raid.unStopable[useHand] = false;
        ctx.anim.SetBool(ctx.raid.currentActionHash,false);
        ctx.anim.SetInteger(ctx.raid.currentProgressHash,0);
    }
}
