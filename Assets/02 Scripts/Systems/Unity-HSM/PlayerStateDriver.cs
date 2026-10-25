using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode.Components;
using Unity.Netcode;
//using UnityUtils;

namespace HSM {
    public class PlayerStateDriver : NetworkBehaviour {
        public PlayerContext ctx = new PlayerContext();
        public SO_PlayerContextLoader pcl;
        public InputReader input;
        [SerializeField] private CharacterController cc;
        [SerializeField] private GameObject body;
        public Transform groundCheck;
        public LayerMask groundMask;
        public bool drawGizmos = true;
        string lastPath;
        
        StateMachine machine;
        State root;

        void Awake() {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            ctx.LoadContext(pcl);

            root = new PlayerRoot(null, ctx);
            var builder = new StateMachineBuilder(root);
            machine = builder.Build();

            ctx.psd = this;
            ctx.controller = cc;
            ctx.body = body;
            
            // fallback: create a groundCheck just below the collider's bounds
            if (groundCheck == null) {
                var col = GetComponent<Collider>();
                var t = new GameObject("groundCheck").transform;
                t.SetParent(transform, false);
                var y = col ? (-col.bounds.extents.y + 0.01f) : -0.5f;
                t.localPosition = new Vector3(0, y, 0);
                groundCheck = t;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                Debug.Log("Owner Set Up");
                SetUpEvents();

                ctx.cinCam = FindFirstObjectByType<CinemachineCamera>();
                
                if(ctx.cinCam != null){
                    ctx.cinCam.Follow = ctx.cameraPosition;
                    ctx.cinCam.LookAt = ctx.cameraPosition;
                }

                ctx.skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                EndEvents();
            }
        }

        void Update() {
            if(!IsOwner) return;

            ctx.grounded = cc.isGrounded;
            if(ctx.grounded) Physics.Raycast(groundCheck.position,Vector3.down,out ctx.groundHit,ctx.groundCheckLength, groundMask);

            machine.Tick(Time.deltaTime);

            var path = StatePath(machine.Root.Leaf());
            ctx.debugCurrentLeaf = machine.Root.Leaf().ToString();

            if (path != lastPath) {
                //Logwin.Log("State", path);
                lastPath = path;
            }

            float gravVel = ctx.velocity.y;
            Vector3 horizontalVel = (ctx.controller.transform.right * ctx.velocity.x) + (ctx.controller.transform.forward * ctx.velocity.z);
            if(ctx.grounded)horizontalVel = AdjustVelocityToSlope(horizontalVel);
            
            
            Vector3 gravVector = new Vector3(0,gravVel,0);
            //Vector3 tempVel = new Vector3(horizontalVel.x,gravVel,horizontalVel.z);
            Vector3 tempVel = horizontalVel + gravVector;

            cc.Move(tempVel * Time.deltaTime);

            // Debug
            ctx.currentVelocityMag = (int)(tempVel.magnitude*100)/100;
            //ctx.debugCurrentDir = tempVel;

        }

        void OnDrawGizmosSelected() {
            if (!drawGizmos || groundCheck == null) return;

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(groundCheck.position, ctx.groundCheckLength);
        }

        static string StatePath(State s) {
            return string.Join(" > ", s.PathToRoot().Reverse().Select(n => n.GetType().Name));
        }

        // Slope ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private Vector3 AdjustVelocityToSlope(Vector3 velocity)
        {
            Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, ctx.groundHit.normal);
            
            // Multiply the rotation by our movement velocity
            Vector3 adjustedVelocity = slopeRotation * velocity;
            
            return adjustedVelocity;
        }

        // Events /////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private void SetUpEvents()
        {
            input.MoveEvent += HandleMove;
            input.JumpEvent += HandleJump;
            input.JumpCancelledEvent += HandleJumpCancelled;
            input.SprintEvent += SprintEvent;
            input.SprintCancelledEvent += SprintEventCancelled;
            input.CrouchEvent += CrouchEvent;
            input.CrouchCancelledEvent += CrouchEventCancelled;
        }
        private void EndEvents()
        {
            input.MoveEvent -= HandleMove;
            input.JumpEvent -= HandleJump;
            input.JumpCancelledEvent -= HandleJumpCancelled;
            input.SprintEvent -= SprintEvent;
            input.SprintCancelledEvent -= SprintEventCancelled;
            input.CrouchEvent -= CrouchEvent;
            input.CrouchCancelledEvent -= CrouchEventCancelled;
        }

        // Handle Events //////////////////////////////////////////////////////////////////////////////////////////////////////
        private void HandleMove(Vector2 moveInput) // Move
        {
            ctx.move.x = moveInput.x;
            ctx.move.z = moveInput.y;

            ctx.move.Normalize();
        }

        private void HandleJump()
        {
            if(!ctx.jumpPressed){
                ctx.jumpPressed = true;
            }
        }
        private void HandleJumpCancelled()
        {
            if(ctx.jumpPressed){
                ctx.jumpPressed = false;
            }
        }
        
        private void SprintEvent()
        {
            if (!ctx.sprinting)
            {
                ctx.sprinting = true;
            }
        }
        private void SprintEventCancelled()
        {
            if (ctx.sprinting)
            {
                ctx.sprinting = false;
            }
        }

        private void CrouchEvent()
        {
            if (!ctx.crouching)
            {
                ctx.crouching = true;
            }
        }
        private void CrouchEventCancelled()
        {
            if (ctx.crouching)
            {
                ctx.crouching = false;
            }
        }
    }

    [Serializable]
    public class PlayerContext {
        public string debugCurrentLeaf;
        public float currentVelocityMag;

        [Header("Game Variables")]
        public Vector3 move;
        public Vector3 velocity;
        
        public float moveSpeed = 6f;
        public float currentMoveSpeed=0f;

        public float accel = 40f;
        public float decel = 50f;
        public float jumpForce = 7f;

        public float gravForce = 9.81f;
        public float drag = 1;

        [Header("State Modifiers")]
        public float sprintMod = 2;
        public float crouchMod = .5f;
        public float groundCheckLength = .5f;
        public RaycastHit groundHit;

        public bool grounded;
        public bool jumpPressed;
        public bool sprinting;
        public bool crouching;

        [Header("Refrences")]
        public GameObject body;
        public CharacterController controller;
        public Transform cameraPosition;
        public CinemachineCamera cinCam;
        public Transform cinCamTransform => cinCam.transform;
        public Animator anim;
        public NetworkAnimator netAnim;
        public SkinnedMeshRenderer skin;
        [HideInInspector] public PlayerStateDriver psd;


        public void LoadContext (SO_PlayerContextLoader loader)
        {
            if(loader.moveSpeed!=0) moveSpeed = loader.moveSpeed;

            if(loader.accel!=0) accel = loader.accel;
            if(loader.decel!=0) decel = loader.decel;
            if(loader.jumpForce!=0) jumpForce = loader.jumpForce;

            if(loader.gravForce !=0) gravForce  = loader.gravForce;
            if(loader.drag!=0) drag = loader.drag;

            if(loader.sprintMod!=0) sprintMod = loader.sprintMod;
            if(loader.crouchMod!=0) crouchMod = loader.crouchMod;
            if(loader.groundCheckLength!=0) groundCheckLength = loader.groundCheckLength;
        }
    }
}