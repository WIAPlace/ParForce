using Unity.Netcode;
using UnityEngine;
using Animancer;
using HSM;


public class NetworkAnimSync : NetworkBehaviour
{
    private Vector2MixerState mixerState;
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
        mixerState = (Vector2MixerState)ctx._anim.States.GetOrCreate(ctx.ach.locomotionTransition);
    }

    public override void OnNetworkSpawn()
    {
       networkInputParam.OnValueChanged += OnInputParamChanged;
        if (IsOwner)
        {
            currentNetMoveState.OnValueChanged += OnMoveStateChange;
            // Play initial state
            PlayStateAnimation(currentNetMoveState.Value);
        }
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
        Debug.Log(newState);
        currentNetMoveState.Value = newState;
        //Debug.Log("Network Hit");
    }

    // fires for all clients when network variable changes
    private void OnInputParamChanged(Vector2 previousValue, Vector2 newValue)
    {
        if (mixerState != null)
        {
            //Debug.Log("Fired");
            // Feed the verified network position into the local Animancer graph
            mixerState.Parameter = newValue;
        }
    }
    public void OnMoveStateChange(MoveState previousState, MoveState newState)
    {
        
        PlayStateAnimation(newState);
    }

    // just a function called if the input vector / move and the network one isnt the same.
    public void NetworkInput(Vector2 target)
    {          
        // Smoothly damp the input vector over time
        currentInput = Vector2.SmoothDamp(currentInput, target, ref smoothVelocity, smoothTime);

        UpdateInputServerRpc(currentInput);
    }

    private void PlayStateAnimation(MoveState state)
    {
        switch (state)
        {
            case MoveState.Idle: ctx._anim.Play(ctx.ach.idleAnimation,.25f); break;
            case MoveState.Move: ctx._anim.Play(ctx.ach.locomotionTransition,.15f); break;
            //case MoveState.Air:  ctx._anim.Play(_run); break;
            default: ctx._anim.Play(ctx.ach.idleAnimation); break;
        }
    }
}
