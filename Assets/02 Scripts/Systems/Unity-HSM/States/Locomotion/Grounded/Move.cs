using UnityEngine;

namespace HSM {
    public class Move : State {
        readonly PlayerContext ctx;
        public readonly Walk Walk;
        public readonly Run Run;
        public readonly MovingCrouch MovingCrouch;

        public Move(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
            this.ctx = ctx;
            Run = new Run(m, this, ctx);
            Walk = new Walk(m, this, ctx);
            MovingCrouch = new MovingCrouch(m, this, ctx);
        }
        protected override State GetInitialState() => Walk;

        protected override State GetTransition() {
            if (!ctx.grounded) return ((LocomotionRoot)Parent).Airborne;
            
            return Mathf.Abs(ctx.move.magnitude) <= 0.01f ? ((Grounded)Parent).Idle : null;
        }

        protected override void OnUpdate(float deltaTime) {
            float rateOfChange = 1f;

            float currentMoveSpeed = ctx.currentMoveSpeed;

            if(ctx.attacking || ctx.blocking)
            {
                currentMoveSpeed /=2;
            }

            float targetx = ctx.move.x * currentMoveSpeed;
            float targetz = ctx.move.z * currentMoveSpeed;

            // Rate of Change
            if(currentMoveSpeed >= ctx.moveSpeed) rateOfChange = ctx.accel;
            else if(currentMoveSpeed < ctx.moveSpeed)rateOfChange = ctx.decel;

            targetx = Mathf.MoveTowards(ctx.velocity.x, targetx, rateOfChange*deltaTime);
            targetz = Mathf.MoveTowards(ctx.velocity.z, targetz, rateOfChange*deltaTime);
            

            ctx.velocity.x = targetx;
            ctx.velocity.z = targetz;
            
        }
    }
}