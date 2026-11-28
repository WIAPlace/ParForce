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
using Animancer.Editor;
using Systems.Inventory.Architecture.Networking;
using Systems.Inventory.Architecture;
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
        public LayerMask itemMask;
        public bool drawGizmos = true;
        string lastLocomotionPath;
        string lastActionPath;
        
        StateMachine locomotionMachine;
        StateMachine actionMachine;
        State locomotionRoot;
        State actionRoot;
        InventoryToEquipmentHUB iteHub;

        [Header("Testing Animancer")]
        public AnimationClip animationClip;
        public AnimancerComponent animancerComponent;

        private NetworkSharedInventoryContainerPanelBinder containerPanelBinder;

        void Awake() {
            //Cursor.visible = false;
            //Cursor.lockState = CursorLockMode.Locked;
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
            
            // fallback: create a groundCheck just below the collider's bounds
            if (groundCheck == null) {
                var col = GetComponent<Collider>();
                var t = new GameObject("groundCheck").transform;
                t.SetParent(transform, false);
                var y = col ? (-col.bounds.extents.y + 0.01f) : -0.5f;
                t.localPosition = new Vector3(0, y, 0);
                groundCheck = t;
            }

            // Animancer
            
            //mixerState = (Vector2MixerState)ctx._anim.Play(locomotionTransition);
            //ctx._anim.Play(ctx.ach.idleAnimation);
            //ctx._anim.Layers[1].SetDebugName("Upper Body");
            ctx.baseLayer = ctx._anim.Layers[0];
            ctx.upperBodyLayer = ctx._anim.Layers[1];
            ctx.upperBodyLayer.Mask = ctx.ach.upperBodyMask;
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

                ctx.skin.material = ctx.mat;

                containerPanelBinder =
                    FindAnyObjectByType<NetworkSharedInventoryContainerPanelBinder>();
                
                iteHub = new InventoryToEquipmentHUB(ctx.equipmentManager);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                EndEvents();
                iteHub.UnInitialize();
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


            if(ctx.nas.currentNetMoveState.Value == MoveState.Move || ctx.nas.currentNetMoveState.Value == MoveState.Crouch){
                Vector2 targetNormalVel = new Vector2(ctx.velocity.x,ctx.velocity.z)/ctx.moveSpeed;

                if(ctx.nas.networkInputParam.Value != targetNormalVel)
                {
                    ctx.nas.NetworkInput(targetNormalVel);
                }
            }
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
            if(!IsOwner) return;
            Gizmos.color = Color.blue;
            
            if(ctx.currentVelocityMag > .01f) Gizmos.DrawLine(ctx.body.transform.position, ctx.body.transform.position + ctx.debugCurrentDir*2);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(ctx.cinCamTransform.position,ctx.cinCamTransform.forward * 3f);

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
            input.InteractCancelledEvent += InteractEventCancelled;
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
            input.InteractCancelledEvent -= InteractEventCancelled;
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
            ctx.actionInputs[1].Add(true,ctx.currentTick);
        }
        private void RightEventCancelled()
        {
            ctx.actionInputs[1].Add(false,ctx.currentTick);
        }
        private void LeftEvent()
        {
            ctx.actionInputs[0].Add(true,ctx.currentTick);
        }
        private void LeftEventCancelled()
        {
            ctx.actionInputs[0].Add(false,ctx.currentTick);
        }

        // Testing With this Button. "E"
        private void InteractEvent()
        {
            if (Physics.SphereCast(ctx.cinCamTransform.position, .1f, ctx.cinCamTransform.forward,
                out RaycastHit hit,itemMask))
            {
                NetworkWorldItem worldItem =
                    hit.collider.GetComponentInParent<NetworkWorldItem>();

                if (worldItem != null &&
                    !worldItem.TryRequestPickup(out string error))
                {
                    Debug.Log(error);
                }

                NetworkSharedInventoryContainer container =
                    hit.collider.GetComponentInParent<NetworkSharedInventoryContainer>();

                if (container != null) InteractWithContainer(container);
                
            }
        }
        private void InteractEventCancelled()
        {
            CloseContainer();
        }

        private bool TryGetContainerPanelBinder()
        {
            // Retry if the HUD wasn't available when the player spawned.
            if (containerPanelBinder == null)
            {
                containerPanelBinder =
                    FindAnyObjectByType<NetworkSharedInventoryContainerPanelBinder>();
            }

            if (containerPanelBinder == null)
            {
                Debug.LogError("Could not find NetworkSharedInventoryContainerPanelBinder.");
                return false;
            }

            return true;
        }
        private void InteractWithContainer(
            NetworkSharedInventoryContainer container)
        {
            if (!IsOwner || !TryGetContainerPanelBinder())
                return;

            if (!containerPanelBinder.TryOpenContainer(
                container, out string error))
            {
                Debug.LogWarning($"Could not open container: {error}");
            }
        }
        private void CloseContainer()
        {
            if (!IsOwner || containerPanelBinder == null)
                return;

            containerPanelBinder.CloseCurrentContainer();
        }

    }

    [Serializable]
    public class PlayerContext {
        [Header("Debug")]
        public string debugCurrentMotionLeaf;
        public string debugCurrentActionLeaf;
        public float currentVelocityMag;
        public bool zeroIsMain;

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
        // will is busy means that certain interactions wont fire.
        public bool[] handIsBusy = new bool[]{false,false};

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
        //public NetworkAnimator netAnim;
        public Material mat;
        public SkinnedMeshRenderer skin;
        public NetworkAnimSync nas;


        [Header("Held Item Refrences")]
        public EquipmentManager equipmentManager;
        public NetworkItemSync sync;
        public HeldItemData[] Hand => equipmentManager.activeItems; // pointer to equipment in equipment manager

        //[Header("Inventory")]
        //public PlayerInventoryOwner inventory;
        //public NetworkInventoryCoordinator coordinator;

        [HideInInspector] public PlayerStateDriver psd;

        [HideInInspector] public Vector3 debugCurrentDir;

        [Header("Actions")]
        public const int hands = 2; // ya, hands are more a matter of abilities rather than actual limbs. for now.

        // We will use this buffer for inputs so that after it occurs it will be thrown out
        // for the action that was taken as the result of this, we will store it in a circular buffer 
        // so that we can refrence it later as needed. 
        public InputBuffer<bool>[] actionInputs = new InputBuffer<bool>[hands]
        {
            new InputBuffer<bool>(12), new InputBuffer<bool>(12)
        }; // 0 = left, // 1 = right

        // Chain buffer
        public ChainBuffer[] chainBuffer = new ChainBuffer[hands] 
        {
            new ChainBuffer(240), new ChainBuffer(240)
        };

        [HideInInspector] public int currentTick=>psd.NetworkManager.NetworkTickSystem.LocalTime.Tick;
        // Index = HandSlot, X = Action Order (1 = prim), Y = State (0 = OnStart , 1 = OnRelease)
        public InputBuffer<Vector2Int>[] requestedAction = new InputBuffer<Vector2Int>[hands]
        {
            new InputBuffer<Vector2Int>(),new InputBuffer<Vector2Int>()
        };
        public Vector2Int[] lastAction = new Vector2Int[hands]; 
        public ExitEvent[] exitEvents = new ExitEvent[hands]
        {
            new ExitEvent(null,null),  new ExitEvent(null,null)
        };
        


        //[Header("Layers")] // use instead of calling _anim, layers corispond to what is going on.
        public AnimancerLayer baseLayer;
        public AnimancerLayer upperBodyLayer;

        public AnimClipHolder ach;


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

        public void BroadcastAction(ushort itemID, HandSlot slot, int actionOrder, int actionTypeCode, int actionDetails) => 
                                                sync.BroadcastAction(itemID, slot, actionOrder, actionTypeCode, actionDetails);
    }

    public enum MoveState{Idle, Move, Air, Crouch, Jump, Land}

    [Serializable]
    public class AnimClipHolder
    {   
        [Header("Clip Assets")]
        public AnimationClip idleAnimation;
        public TransitionAsset locomotionTransition;
        public TransitionAsset crouchTransition;
        public AnimationClip airAnimation;
        public AnimationClip jumpAnimation;
        public AnimationClip landAnimation;
        [Header("Masks")]
        public AvatarMask upperBodyMask;

        
    }

    
}