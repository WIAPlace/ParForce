using UnityEngine;

namespace HSM {
    public class Grounded : State {
        readonly PlayerContext ctx;
        public readonly Idle Idle;
        public readonly Move Move;

        public Grounded(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
            this.ctx = ctx;
            Idle = new Idle(m, this, ctx);
            Move = new Move(m, this, ctx);
        }
        
        protected override State GetInitialState() => Idle;

        protected override State GetTransition() {
            if (ctx.jumpPressed) {
                ctx.jumpPressed = false;
                ctx.velocity.y = ctx.jumpForce;
                return ((PlayerRoot)Parent).Airborne;
            }
            return ctx.grounded ? null : ((PlayerRoot)Parent).Airborne;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if(ctx.grounded && ctx.velocity.y < 0) // if on ground reset gravity
            {
                ctx.velocity.y = -2;
            }

            Vector3 targetRotation = new Vector3(0, ctx.cinCamTransform.eulerAngles.y, 0);
            ctx.controller.transform.rotation = Quaternion.Euler(targetRotation);
        }

    }
}