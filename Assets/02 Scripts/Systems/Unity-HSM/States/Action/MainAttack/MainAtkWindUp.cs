using HSM;
using UnityEngine;

public class MainAtkWindUp : State
{
    readonly PlayerContext ctx;

    private float startTime;


    public MainAtkWindUp(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if (!ctx.attacking)
        {
            float holdDuration = Time.time - startTime; 
            if(holdDuration>= ctx.heavyHoldThreshold)
            {
                return ((MainAttack)Parent).Heavy;
            }
            else return ((MainAttack)Parent).Light;
        }
        return null;
    }

    protected override void OnEnter()
    {
        // Play Wind Up animation
        startTime = Time.time;
    }

    //protected override 
}
