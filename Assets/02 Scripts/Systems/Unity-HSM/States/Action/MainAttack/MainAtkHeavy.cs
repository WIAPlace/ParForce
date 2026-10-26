using HSM;
using UnityEngine;

public class MainAtkHeavy : State
{
    readonly PlayerContext ctx;
    float startTime;
    public MainAtkHeavy(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        float elapsedTime = Time.time - startTime;
        if(elapsedTime >= ctx.heavyAtkTime) return ((ActionRoot)Parent.Parent).Idle;
        return base.GetTransition();
    }

    protected override void OnEnter()
    {
        // Play Animation 
        startTime = Time.time;
        base.OnEnter();
    }

    protected override void OnExit()
    {
        base.OnExit();
    }
}
