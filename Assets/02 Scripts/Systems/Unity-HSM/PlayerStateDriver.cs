using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode.Components;
using Unity.Netcode;
using NUnit.Framework;
using UnityEngine.XR;
using Unity.VisualScripting;
using Animancer;
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
        string lastLocomotionPath;
        string lastActionPath;
        
        StateMachine locomotionMachine;
        StateMachine actionMachine;
        State locomotionRoot;
        State actionRoot;

        [Header("Testing Animancer")]
        public AnimationClip animationClip;
        public AnimancerComponent animancerComponent;


        

        void Awake() {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            ctx.LoadContext(pcl);

            locomotionRoot = new LocomotionRoot(null, ctx);
            var locoBuilder = new StateMachineBuilder(locomotionRoot);
            locomotionMachine = locoBuilder.Build();

            actionRoot = new ActionRoot(null, ctx);
            var actionBuilder = new StateMachineBuilder(actionRoot);
            actionMachine = actionBuilder.Build();

            ctx.psd = this;
            ctx.controller = cc;
            ctx.body = body;

            // set up arrays to be the needed length
            int numHands = ctx.Hand.Length;
            ctx.rad.handChainNum = new int[numHands];
            ctx.rad.blockHandInput = new bool[numHands];
            ctx.raid.chainLength = new int[numHands][];
            ctx.raid.chainWindow = new float[numHands][];
            ctx.raid.unStopable = new bool[numHands];
            
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

                //ctx.skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

                ctx.sync.OnStart();
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

            //Check if grounded, then get info about the ground if so.
            if(cc.isGrounded || Physics.CheckSphere(groundCheck.position, ctx.groundCheckLength, groundMask)) ctx.grounded = true;
            else ctx.grounded = false;
            if(ctx.grounded) Physics.Raycast(groundCheck.position,Vector3.down,out ctx.groundHit,ctx.groundCheckLength+.2f, groundMask);

            MachineTick(); // run the state machines.

            // Movement Functions
            float gravVel = ctx.velocity.y;
            Vector3 horizontalVel = (ctx.controller.transform.right * ctx.velocity.x) + (ctx.controller.transform.forward * ctx.velocity.z);
            if(ctx.grounded) horizontalVel = AdjustVelocityToSlope(horizontalVel);
            

            Vector3 gravVector = new Vector3(0,gravVel,0);
            //Vector3 tempVel = new Vector3(horizontalVel.x,gravVel,horizontalVel.z);
            Vector3 tempVel = horizontalVel + gravVector;

            cc.Move(tempVel * Time.deltaTime);

            // Debug
            ctx.currentVelocityMag = (int)(tempVel.magnitude*100)/100;
            ctx.debugCurrentDir = tempVel;

        }

        private void MachineTick()
        {
            // run the machines
            locomotionMachine.Tick(Time.deltaTime);
            actionMachine.Tick(Time.deltaTime);

            // store where we are in each of them
            var motionPath = StatePath(locomotionMachine.Root.Leaf());
            ctx.debugCurrentMotionLeaf = locomotionMachine.Root.Leaf().ToString();
            var actionPath = StatePath(actionMachine.Root.Leaf());
            ctx.debugCurrentActionLeaf = actionMachine.Root.Leaf().ToString();

            // usefull if needed.
            if (motionPath != lastLocomotionPath) {
                //Logwin.Log("State", path);
                lastLocomotionPath = motionPath;
            }
            if (actionPath != lastActionPath) {
                //Logwin.Log("State", path);
                lastActionPath = actionPath;
            }
        }
       
        // Gizmos /////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
            
            if(ctx.currentVelocityMag > .01f) Gizmos.DrawLine(ctx.body.transform.position, ctx.body.transform.position + ctx.debugCurrentDir*2);
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
            // 1. Get the normal of the slope
            Vector3 slopeNormal = ctx.groundHit.normal;

            // 2. Project your raw movement velocity onto the slope plane
            Vector3 adjustedVelocity = Vector3.ProjectOnPlane(velocity, slopeNormal);

            // 3. Keep the original speed to prevent moving too fast/slow on slopes
            return adjustedVelocity.normalized * velocity.magnitude;
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
            input.AttackEvent += LeftEvent;
            input.AttackCancelledEvent += LeftEventCancelled;
            input.BlockEvent += RightEvent;
            input.BlockCancelledEvent += RightEventCancelled;
            input.InteractEvent += InteractEvent;
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
            input.AttackEvent -= LeftEvent;
            input.AttackCancelledEvent -= LeftEventCancelled;
            input.BlockEvent -= RightEvent;
            input.BlockCancelledEvent -= RightEventCancelled;
            input.InteractEvent -= InteractEvent;
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
        
        

        private void RightEvent()
        {
            if(!ctx.rad.blockHandInput[1] && !ctx.rightDown && !ctx.leftDown) {
                ctx.rightDown = true;
                EvaluateHandActions(false,ctx.rightDown);
            }
        }
        private void RightEventCancelled()
        {
            if(ctx.rightDown) {
                ctx.rightDown = false;
                EvaluateHandActions(false,ctx.rightDown);
            }
        }
        private void LeftEvent()
        {
            if(!ctx.rad.blockHandInput[0] && !ctx.leftDown && !ctx.rightDown){
                ctx.leftDown = true;
                EvaluateHandActions(true,ctx.leftDown);
            }
        }
        private void LeftEventCancelled()
        {
            if(ctx.leftDown){
                ctx.leftDown = false;
                EvaluateHandActions(true,ctx.leftDown);
            }
        }

        // Evaluates which hand for action type and switching states
        // if we need to have right only or left only slot type clauses, fix that later.
        private void EvaluateHandActions(bool isLeft, bool down)
        {
            int hand = isLeft ? 0 : 1;
            int offHand = isLeft? 1 : 0;

            int chosenHand = -1; // -1 means neither hand met the condition

            if (ctx.Hand[hand] != null && ctx.Hand[hand].slotType != SlotType.Empty)
            {
                chosenHand = hand;

                if(ctx.Hand[hand].slotType == SlotType.Both) ctx.rad.hand = HandSlot.TwoHanded;
                else ctx.rad.hand = (HandSlot)chosenHand;

                if(down){
                    ctx.rad.type = ctx.Hand[hand].primaryAction.actionType;
                    ctx.rad.order = ActionOrder.Prim;
                    return;
                }
            }
            else if (ctx.Hand[offHand] != null && ctx.Hand[offHand].slotType != SlotType.Empty && ctx.Hand[offHand].secondaryAction != null)
            {
                chosenHand = offHand;

                if(ctx.Hand[offHand].slotType == SlotType.Both) ctx.rad.hand = HandSlot.TwoHanded;
                else ctx.rad.hand = (HandSlot)chosenHand;

                if(down){
                    ctx.rad.type = ctx.Hand[offHand].secondaryAction.actionType;
                    ctx.rad.order = ActionOrder.Sec;
                    return;
                }
            }
            //else return;
            if(chosenHand >= 0 && !ctx.raid.unStopable[chosenHand]) ctx.rad.type = ActionType.None;

            // Check if either condition passed
            if (chosenHand == -1) {
                ctx.rad.order = ActionOrder.None;
                ctx.rad.type = ActionType.None;
                return;
            }
        }

        bool test=false;
        // Testing With this Button. "E"
        private void InteractEvent()
        {
            if(!test){
                ctx.equipmentManager.leftHandItemID.Value = 10; 
                test=true;
                return;
            }

            ctx.Hand[0].primaryAction.OnLocalStart(HandSlot.LeftHand, ctx, ctx.Hand[0].id);
        }


    }

    [Serializable]
    public class PlayerContext {
        [Header("Debug")]
        public string debugCurrentMotionLeaf;
        public string debugCurrentActionLeaf;
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

        [Header("Attacks")]
        public float heavyHoldThreshold = .4f;
        public float lightAtkTime = .3f;
        public float heavyAtkTime = .5f;

        [Header("Sheild")]
        public float shieldHeftTime=.2f;
        public float heftTime = 0;

        [Header("Bools")]
        public bool grounded;
        public bool jumpPressed;
        public bool sprinting;
        public bool crouching;
        public bool rightDown;
        public bool leftDown;
        public bool shieldActive;
        //public bool blocking;
        //public bool attacking;

        //public ActionType rad.type;
        //public HandSlot rad.hand;

        [Header("Refrences")]
        public GameObject body;
        public CharacterController controller;
        public Transform cameraPosition;
        public CinemachineCamera cinCam;
        public Transform cinCamTransform => cinCam.transform;
        public Animator anim;
        public AnimancerComponent _anim;
        public NetworkAnimator netAnim;
        public SkinnedMeshRenderer skin;

        [Header("Held Item Refrences")]
        public EquipmentManager equipmentManager;
        public NetworkItemSync sync;
        public HeldItemData[] Hand => equipmentManager.activeItems; // pointer to equipment in equipment manager
        
        [HideInInspector] public PlayerStateDriver psd;

        [HideInInspector] public Vector3 debugCurrentDir;

        [Header("Debugging For RAD & RAID")]
        // Holder for information sent between player inputs and actions HSM.
        public RequestedActionData rad = new RequestedActionData();
        public RequestedActionItemData raid = new RequestedActionItemData();

        

        public static readonly int MotionHash = Animator.StringToHash("Motion");
        public static readonly int IsCrouchHash = Animator.StringToHash("IsCrouch");
        public static readonly int IsAttackHash = Animator.StringToHash("IsGrab");
        public static readonly int IsHit = Animator.StringToHash("IsHit");


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

            heavyHoldThreshold = loader.heavyHoldThreshold;
            lightAtkTime = loader.lightAtkTime;
            heavyAtkTime = loader.heavyAtkTime;

            shieldHeftTime = loader.shieldHeftTime;
        }

        public float GetAnimClipLength(int layer)
        {
            return anim.GetCurrentAnimatorStateInfo(layer).length;
        }

        public void BroadcastAction(ushort itemID, HandSlot slot, int actionTypeCode) => sync.BroadcastAction(itemID, slot, actionTypeCode);
    }

    [Serializable] // Data requested by player to the Action State Machine
    public class RequestedActionData
    {
        public HandSlot hand;
        public ActionType type;
        public ActionOrder order; // 0 = Primary, 1 = secondary 

        public int[] handChainNum; // counter for the current chain

        public bool[] blockHandInput;

    }
    
    [Serializable] // Data requested by the ItemAction to the State Machine. // ya i know its a weird name, raid is just easier to type than riad
    public class RequestedActionItemData
    {
        public int currentActionHash = 0;
        public int currentProgressHash = 0;
        public int[][] chainLength;
        public float[][] chainWindow;
        public bool[] unStopable;
    }
}