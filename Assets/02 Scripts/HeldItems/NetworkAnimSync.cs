using Unity.Netcode;
using UnityEngine;
using Animancer;
using HSM;
using UnityEngine.AI;



public class NetworkAnimSync : NetworkBehaviour
{
    private Vector2MixerState walkState;
    private Vector2MixerState crouchState;
    private Vector2 currentInput;
    private Vector2 smoothVelocity;
    [SerializeField] private float smoothTime = 0.1f; // Adjust for faster or slower fade
    [SerializeField] private PlayerStateDriver psd;
    private PlayerContext ctx;

    // Create an unmanaged NetworkVariable to sync the 2D blend space position
    public readonly NetworkVariable<Vector2> networkInputParam = new NetworkVariable<Vector2>(
        default, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server // Server-Authoritative
    );
    // Sync the state from Server to Clients
    public NetworkVariable<MoveState> currentNetMoveState = new NetworkVariable<MoveState>(
        MoveState.Idle, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        ctx = psd.ctx;
        walkState = (Vector2MixerState)ctx._anim.States.GetOrCreate(ctx.ach.locomotionTransition);
        crouchState = (Vector2MixerState)ctx._anim.States.GetOrCreate(ctx.ach.crouchTransition);
    }

    public override void OnNetworkSpawn()
    {
       networkInputParam.OnValueChanged += OnInputParamChanged;
       currentNetMoveState.OnValueChanged += OnMoveStateChange;

       // Play initial state
        PlayStateAnimation(currentNetMoveState.Value);
        
    }

    public override void OnNetworkDespawn()
    {
        networkInputParam.OnValueChanged -= OnInputParamChanged;
        currentNetMoveState.OnValueChanged -= OnMoveStateChange;
    }
        
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Network Events /////////////////////////////////////////////////////////////////////////////////
    [ServerRpc]
    private void UpdateInputServerRpc(Vector2 input)
    {
        
        networkInputParam.Value = input;
    }

    [ServerRpc]
    public void UpdateMoveStateServerRpc(MoveState newState)
    {
        currentNetMoveState.Value = newState;
        //Debug.Log("Network Hit");
    }

    // fires for all clients when network variable changes
    private void OnInputParamChanged(Vector2 previousValue, Vector2 newValue)
    {
        switch (currentNetMoveState.Value)
        {
            case MoveState.Move:
                if(walkState!=null) walkState.Parameter = newValue;
                break;
            case MoveState.Crouch: 
                if(crouchState!=null) crouchState.Parameter = newValue;
                break;
            default: break;
        }
        
    }
    public void OnMoveStateChange(MoveState previousState, MoveState newState)
    {
        
        PlayStateAnimation(newState);
    }

    // just a function called if the input vector / move and the network one isnt the same.
    public void NetworkInput(Vector2 target)
    {
        currentInput = Vector2.SmoothDamp(
            currentInput,
            target,
            ref smoothVelocity,
            smoothTime
        );

        if (Vector2.Distance(currentInput, networkInputParam.Value) > 0.05f)
        {
            UpdateInputServerRpc(currentInput);
        }
    }

    private void PlayStateAnimation(MoveState state)
    {
        switch (state)
        {
            case MoveState.Idle: ctx._anim.Play(ctx.ach.idleAnimation,.25f); break;
            case MoveState.Move: ctx._anim.Play(ctx.ach.locomotionTransition,.15f); break;
            case MoveState.Crouch: ctx._anim.Play(ctx.ach.crouchTransition,.15f); break;

            case MoveState.Air:  ctx._anim.Play(ctx.ach.airAnimation,.15f); break;
            case MoveState.Jump: 
                // play inital jump anim
                AnimancerState animState = ctx._anim.Play(ctx.ach.jumpAnimation,.15f);

                // Register callback once it has finished to change to air;
                animState.Events(this).OnEnd = () =>
                {
                    ctx._anim.Play(ctx.ach.airAnimation,.25f);
                };
                break;

            default: 
                ctx._anim.Play(ctx.ach.idleAnimation); 
                break;
        }
        // We could posibly look into a sort of priority que, so while something is playing, it will play the next thing after. idk
    }
}
