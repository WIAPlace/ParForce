using HSM;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

public class NetworkItemSync : NetworkBehaviour
{
    [SerializeField] private PlayerStateDriver psd;
    private PlayerContext ctx;
    private EquipmentManager localEquipment;
    private Animator animator;

    // things for hit ditection
    [SerializeField] private LayerMask targetLayers;
    private bool isTracing;
    private HeldItemData currentItem;
    private Vector3[] previousWorldPoints;
    private readonly HashSet<NetworkObject> hitTargets = new HashSet<NetworkObject>();
    private int currentSwingId;
    private float currentRadius;

    [Header("Temp for Hand")]
    [SerializeField] private Transform weaponSocket;

    public void OnStart()
    {
        ctx = psd.ctx;
        localEquipment = ctx.equipmentManager;
        localEquipment.animancer = ctx._anim;
        animator = ctx.anim;
    }
    
    // 1. The local player calls this from their input script or ScriptableObject action.
    public void BroadcastAction(ushort itemID, HandSlot slot, int actionTypeCode)
    {
        // If we are the Host/Server, we can bypass the RPC and go straight to the clients
        if (IsServer)
        {
            ExecuteActionClientRpc(itemID, slot, actionTypeCode);
        }
        else
        {
            // If we are a client, request that the server/host broadcasts this action
            ExecuteActionServerRpc(itemID, slot, actionTypeCode);
        }
    }

    // 2. The Host/Server receives the input execution request from a client.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExecuteActionServerRpc(ushort itemID, HandSlot slot, int actionTypeCode)
    {
        // Host immediately forwards this down to all other players in the session
        ExecuteActionClientRpc(itemID, slot, actionTypeCode);
    }

    // 3. Every connected machine executes this code to update puppet visuals.
    [Rpc(SendTo.Everyone)]
    private void ExecuteActionClientRpc(ushort itemID, HandSlot slot, int actionTypeCode)
    {
        // Safety First: Avoid running this on the client who originally swung the sword.
        // Their game already played the animation instantly on frame one!
        //if (IsOwner) return; 
        if (HeldItemDatabase.Instance == null) return;

        // CRITICAL DATA LINK: Look up the structural asset locally using the network integer ID
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);
        
        if (item != null && item.primaryAction is ItemAction netAction)
        {
            // Run the remote server logic
            netAction.OnRemoteExecute(slot, psd.ctx ,actionTypeCode,IsServer,IsClient);
        }
    }

    // Casual client-authoritative damage processor running straight through the host. 
    [Rpc(SendTo.Server)]
    public void ApplyDamageServerRpc(NetworkObjectReference targetNetObj, float damage)
    {
        if (targetNetObj.TryGet(out NetworkObject netObj))
        {
            // Host applies the health deduction to the target (enemy or player)
            // e.g., netObj.GetComponent<Health>().ModifyHealth(-damage);
            Debug.Log($"[Host] Applied {damage} damage to network entity: {netObj.name}");
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////////////////// Melee Tracing
    // Melee Tracing for Server based hit ditection
    public int StartTrace(HeldItemData weapon)//////////////////////////////////////////// Start
    {
        if(isTracing) StopTrace(currentSwingId);
        if (!IsServer || weapon == null || weapon.localPoints.Length == 0 ) return -1;

        currentSwingId++; // update the ID so that hit ques wont overlap
        currentItem = weapon;
        hitTargets.Clear();

        // Initialize previous points to the exact starting position of the active swing
        int count = weapon.localPoints.Length;
        previousWorldPoints = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            previousWorldPoints[i] = weaponSocket.TransformPoint(weapon.localPoints[i]);
        }
        
        currentRadius = weapon.pointRadius;

        isTracing = true;
        return currentSwingId;
    }
    public void StopTrace(int swingId)////////////////////////////////////////////////// Stop
    {
        if (!IsServer) return;

        if(swingId == currentSwingId){
            isTracing = false;
            currentItem = null;
        }
    }

    // LateUpdate ensures Animancer has already updated bone transforms for this frame
    private void LateUpdate()///////////////////////////////////////////////////////////// Late Update
    {
        if (!IsServer || !isTracing || currentItem == null) return;

        var localPoints = currentItem.localPoints;

        for (int i = 0; i < localPoints.Length; i++)
        {
            Vector3 currentWorldPoint = weaponSocket.TransformPoint(localPoints[i]);
            Vector3 prevWorldPoint = previousWorldPoints[i];

            Vector3 direction = currentWorldPoint - prevWorldPoint;
            float distance = direction.magnitude;

            if (distance > 0.001f)
            {
                // Cast a ray from last frame's point to this frame's point
                if (Physics.SphereCast(prevWorldPoint, currentRadius, direction.normalized, out RaycastHit hit, distance, targetLayers))
                {
                    ProcessHit(hit.collider);
                }

                // Optional: Visualize the swing arc in the Server Scene view
                Debug.DrawLine(prevWorldPoint, currentWorldPoint, Color.red, 1.0f);
            }

            // Store current position as the previous position for the next frame
            previousWorldPoints[i] = currentWorldPoint;
        }
    }

    
    private void ProcessHit(Collider hitCollider) /////////////////////////////////////////// Process Hit
    {
        var targetNetObj = hitCollider.GetComponentInParent<NetworkObject>();
        if (targetNetObj == null || targetNetObj == NetworkObject) return;

        // Ensure each target is only damaged once per swing
        if (hitTargets.Contains(targetNetObj)) return;
        hitTargets.Add(targetNetObj);

        if (targetNetObj.TryGetComponent<IHitable>(out var damageable))
        {
            damageable.OnHit();
        }
    }

    void OnDrawGizmos()
    {
        if(currentItem==null) return;
        Gizmos.color = Color.azure;

        var localPoints = currentItem.localPoints;
        
        for (int i = 0; i < localPoints.Length; i++)
        {
            Gizmos.DrawWireSphere(weaponSocket.TransformPoint(localPoints[i]),currentItem.pointRadius);
        }
    }


}
