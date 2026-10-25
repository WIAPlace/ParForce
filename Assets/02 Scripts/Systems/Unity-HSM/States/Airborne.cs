using UnityEngine;

namespace HSM {
    public class Airborne : State {
        readonly PlayerContext ctx;

        public Airborne(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
            this.ctx = ctx;
        }
        
        protected override State GetTransition() => ctx.grounded ? ((PlayerRoot)Parent).Grounded : null;

        protected override void OnEnter() {
            //Debug.Log("Entered");
        }

        protected override void OnExit()
        {
            base.OnExit();
        }

        protected override void OnUpdate(float deltaTime)
        {
            //Debug.Log("Updating");
            //Debug.Log(ctx.velocity + "  (Update)");
            if(ctx.grounded && ctx.velocity.y < 0) // if on ground reset gravity
            {
                ctx.velocity.y = 0;
            }
            //Debug.Log("grav");
           
            ctx.velocity.y += -ctx.gravForce * deltaTime; // apply gravity

            ctx.velocity.x /= 1f + ctx.drag * deltaTime;
            ctx.velocity.z /= 1f + ctx.drag * deltaTime;

            if(Mathf.Abs(ctx.velocity.x) < .01f)
            {
                ctx.velocity.x = 0;
            }
            if(Mathf.Abs(ctx.velocity.z) < .01f )
            {
                ctx.velocity.z = 0;
            }

            Vector3 targetRotation = new Vector3(0, ctx.cinCamTransform.eulerAngles.y, 0);
            ctx.controller.transform.rotation = Quaternion.Euler(targetRotation);
        }
    }
}