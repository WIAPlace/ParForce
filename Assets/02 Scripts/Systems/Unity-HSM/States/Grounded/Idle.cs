using UnityEngine;

namespace HSM {
    public class Idle : State {
        readonly PlayerContext ctx;
        public readonly IdleCrouch IdelCrouch;
        public readonly Stand Stand;

        public Idle(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
            this.ctx = ctx;
            IdelCrouch = new IdleCrouch(m, this, ctx);
            Stand = new Stand(m, this, ctx);
        }

        protected override State GetInitialState() => Stand;

        protected override State GetTransition() {
            return Mathf.Abs(ctx.move.magnitude) > 0.01f ? ((Grounded)Parent).Move : null;
        }

        protected override void OnEnter() {
            //ctx.currentMoveSpeed = 0;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if(ctx.velocity.magnitude > 0.1f)
            {
                //Debug.Log("Decelling: " + ctx.velocity);
                float currentX = Mathf.MoveTowards(ctx.velocity.x,0,ctx.decel * deltaTime);
                float currentZ = Mathf.MoveTowards(ctx.velocity.z,0,ctx.decel * deltaTime);

                ctx.velocity.x = currentX;
                ctx.velocity.z = currentZ;
                if(Mathf.Abs(ctx.velocity.x) < .01f)
                {
                    ctx.velocity.x = 0;
                }
                if(Mathf.Abs(ctx.velocity.z) < .01f)
                {
                    ctx.velocity.z = 0;
                }
            }
            else if(ctx.velocity.magnitude != 0 &&ctx.velocity.magnitude < 0.1f)
            {
                ctx.velocity = Vector3.zero;
            }
        }
    }
}