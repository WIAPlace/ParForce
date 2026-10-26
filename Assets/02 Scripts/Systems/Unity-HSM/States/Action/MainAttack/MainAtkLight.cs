using HSM;
using UnityEngine;

public class MainAtkLight : State
{
    readonly PlayerContext ctx;
    private float startTime;

    public MainAtkLight(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        float elapsedTime = Time.time - startTime;
        if(elapsedTime >= ctx.lightAtkTime) return ((ActionRoot)Parent.Parent).Idle;
        return base.GetTransition();
    }

    protected override void OnEnter()
    {
        // Play Animation
        startTime = Time.time;
    }

    protected override void OnExit()
    {
        base.OnExit();
    }
}
