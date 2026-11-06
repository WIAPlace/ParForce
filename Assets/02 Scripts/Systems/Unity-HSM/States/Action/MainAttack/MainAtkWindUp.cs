using HSM;
using UnityEngine;
using UnityEngine.LightTransport;

public class MainAtkWindUp : State
{
    readonly PlayerContext ctx;

    private float startTime;
    float windTime;

    bool unStopableHolder;
    int useHand;

    public MainAtkWindUp(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
    }

    protected override State GetTransition()
    {
        if (ctx.rad.type != ActionType.Attack)
        {
            float holdDuration = Time.time - startTime; 
            if(holdDuration >= windTime)
            {
                Debug.Log("Heavy");
                return ((MainAttack)Parent).Heavy;
                
            }
            else {
                Debug.Log("Light");
                return ((MainAttack)Parent).Light;
            }
        }
        return null;
    }

    protected override void OnEnter()
    {
        // Play Wind Up animation 
        useHand = (int)ctx.rad.hand;

        unStopableHolder = ctx.raid.unStopable[useHand];
        ctx.raid.unStopable[useHand] = false;

        windTime = ctx.GetAnimClipLength(useHand+1);
        //Debug.Log(windTime);

        startTime = Time.time;
    }
    protected override void OnExit()
    {
        ctx.rad.handChainNum[useHand] = 1;
        ctx.rad.type = ActionType.None;
        ctx.raid.unStopable[useHand] = unStopableHolder;
    }

    //protected override 
}
