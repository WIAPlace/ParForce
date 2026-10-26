using UnityEngine;
using HSM;
public class ActionRoot : State {
    readonly PlayerContext ctx;
    public readonly IdleAction Idle;
    public readonly MainAttack MainAttack;
    public readonly Block Block;


    public ActionRoot(StateMachine m, PlayerContext ctx) : base(m, null) {
        this.ctx = ctx;
        Idle = new IdleAction(m, this, ctx);
        MainAttack = new MainAttack(m, this, ctx);
        Block = new Block(m, this, ctx);
    }

    protected override State GetInitialState()=>Idle;
}
