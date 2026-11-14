using UnityEngine;
using HSM;
public class ActionRoot : State {
    readonly PlayerContext ctx;
    


    public ActionRoot(StateMachine m, PlayerContext ctx) : base(m, null) {
        this.ctx = ctx;
        
    }

   // protected override State GetInitialState()=>Idle;
}
