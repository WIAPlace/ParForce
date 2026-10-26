using UnityEngine;
using HSM;

public class MainAttack : State {
    readonly PlayerContext ctx;
    public readonly MainAtkWindUp WindUp;
    public readonly MainAtkLight Light;
    public readonly MainAtkHeavy Heavy;

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
        base.OnEnter();
    }

    protected override void OnExit()
    {
        base.OnExit();
    }
}
