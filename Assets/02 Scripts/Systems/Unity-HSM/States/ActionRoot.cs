using UnityEngine;
using HSM;
public class ActionRoot : State {
    readonly PlayerContext ctx;
    public readonly HeldItemsAction HeldItems;

    public ActionRoot(StateMachine m, PlayerContext ctx) : base(m, null) {
        this.ctx = ctx;
        HeldItems = new HeldItemsAction(m, this, ctx);
    }

    protected override State GetInitialState()=>HeldItems;
}
