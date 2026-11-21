using HSM;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class NetworkItemSync : NetworkBehaviour
{
    [SerializeField] private PlayerStateDriver psd;
    private PlayerContext ctx;
    private EquipmentManager localEquipment;
    private Animator animator;

    // things for hit ditection
    [SerializeField] private LayerMask targetLayers;

    public struct CurrentItemInfo{
        public bool isTracing;
        public HeldItemData currentItem;
        public Vector3[] previousWorldPoints;
        public readonly HashSet<NetworkObject> hitTargets;// = new HashSet<NetworkObject>();
        public int currentSwingId;
        public float currentRadius;
        public Transform weaponSocket;

        public CurrentItemInfo(HeldItemData newItem=null)
        {
            isTracing = false;
            currentItem = newItem;
            previousWorldPoints=new Vector3[3];
            hitTargets = new HashSet<NetworkObject>();
            currentSwingId = 0;
            currentRadius = .01f;
            weaponSocket = null;
        }
    } 
    private CurrentItemInfo[] currentItems = new CurrentItemInfo[PlayerContext.hands]
    {
        new CurrentItemInfo(null),new CurrentItemInfo(null)
    };


    //[Header("Temp for Hand")]
    //[SerializeField] private Transform weaponSocket;

    public void OnStart()
    {
        ctx = psd.ctx;
        localEquipment = ctx.equipmentManager;
        localEquipment.animancer = ctx._anim;
        animator = ctx.anim;
    }
    
    // 1. The local player calls this from their input script or ScriptableObject action.
    public void BroadcastAction(ushort itemID, HandSlot slot, int actionOrder, int actionTypeCode,int actionDetails)
    {
        // If we are the Host/Server, we can bypass the RPC and go straight to the clients
        if (IsServer)
        {
            ExecuteActionClientRpc(itemID, slot,actionOrder, actionTypeCode, actionDetails);
        }
        else
        {
            // If we are a client, request that the server/host broadcasts this action
            ExecuteActionServerRpc(itemID, slot,actionOrder, actionTypeCode, actionDetails);
        }
    }

    // 2. The Host/Server receives the input execution request from a client.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExecuteActionServerRpc(ushort itemID, HandSlot slot, int actionOrder, int actionTypeCode, int actionDetails)
    {
        // Host immediately forwards this down to all other players in the session
        ExecuteActionClientRpc(itemID, slot, actionOrder, actionTypeCode, actionDetails);
    }

    // 3. Every connected machine executes this code to update puppet visuals.
    [Rpc(SendTo.Everyone)]
    private void ExecuteActionClientRpc(ushort itemID, HandSlot slot, int actionOrder, int actionTypeCode, int actionDetails)
    {
        // Safety First: Avoid running this on the client who originally swung the sword.
        // Their game already played the animation instantly on frame one!
        //if (IsOwner) return; 
        if (HeldItemDatabase.Instance == null) return;

        // CRITICAL DATA LINK: Look up the structural asset locally using the network integer ID
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);
        
        // item.actions[ctx.lastAction[(int)slot].x] is basicly just a guess that this should work. its the current order given. of what is likley this hand.
        // should hopefully work // it doesnt.
        //Debug.Log(actionOrder);
        if (item != null && item.actions[actionOrder] is ItemAction netAction)
        {
            // Run the remote server logic
            netAction.OnRemoteExecute(slot, psd.ctx, actionTypeCode, actionDetails, IsServer, IsClient);
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
    public int StartTrace(HeldItemData weapon,int hand)//////////////////////////////////////////// Start
    {
        if(currentItems[hand].isTracing) StopTrace(currentItems[hand].currentSwingId,hand);
        if (!IsServer || weapon == null || weapon.localPoints.Length == 0 ) return -1;

        currentItems[hand].currentSwingId++; // update the ID so that hit ques wont overlap
        currentItems[hand].currentItem = weapon;
        currentItems[hand].hitTargets.Clear();
        
        int sign=1;
        // Set where the weapon should originate from
        if(hand<2){
            currentItems[hand].weaponSocket = hand==0 ? ctx.equipmentManager.leftHandBone : ctx.equipmentManager.rightHandBone;
            sign = (hand*2)-1;
        }
        // Initialize previous points to the exact starting position of the active swing
        int count = weapon.localPoints.Length;
        currentItems[hand].previousWorldPoints = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            currentItems[hand].previousWorldPoints[i] = currentItems[hand].weaponSocket.TransformPoint(weapon.localPoints[i]*sign);
        }
        
        currentItems[hand].currentRadius = weapon.pointRadius;

        currentItems[hand].isTracing = true;
        return currentItems[hand].currentSwingId;
    }
    public void StopTrace(int swingId,int hand)////////////////////////////////////////////////// Stop
    {
        if (!IsServer) return;

        if(swingId == currentItems[hand].currentSwingId){
            currentItems[hand].isTracing = false;
            currentItems[hand].currentItem = null;
        }
    }

    // LateUpdate ensures Animancer has already updated bone transforms for this frame
    private void LateUpdate()///////////////////////////////////////////////////////////// Late Update
    {
        if(currentItems==null) return;
        for(int hand = 0; hand < currentItems.Length;hand++){
            if (!IsServer || !currentItems[hand].isTracing || currentItems[hand].currentItem == null) continue;

            var localPoints = currentItems[hand].currentItem.localPoints;

            int sign = hand<2 ? (hand*2)-1 : 1;

            for (int i = 0; i < localPoints.Length; i++)
            {
                Vector3 currentWorldPoint = currentItems[hand].weaponSocket.TransformPoint(localPoints[i]*sign);
                Vector3 prevWorldPoint = currentItems[hand].previousWorldPoints[i];

                Vector3 direction = currentWorldPoint - prevWorldPoint;
                float distance = direction.magnitude;

                if (distance > 0.001f)
                {
                    // Cast a ray from last frame's point to this frame's point
                    if (Physics.SphereCast(prevWorldPoint, currentItems[hand].currentRadius, 
                                direction.normalized, out RaycastHit hit, distance, targetLayers))
                    {
                        ProcessHit(hit.collider,hand);
                    }

                    // Optional: Visualize the swing arc in the Server Scene view
                    Debug.DrawLine(prevWorldPoint, currentWorldPoint, Color.red, 1.0f);
                }

                // Store current position as the previous position for the next frame
                currentItems[hand].previousWorldPoints[i] = currentWorldPoint;
            }
        }
    }

    
    private void ProcessHit(Collider hitCollider,int hand) /////////////////////////////////////////// Process Hit
    {
        var targetNetObj = hitCollider.GetComponentInParent<NetworkObject>();
        if (targetNetObj == null || targetNetObj == NetworkObject) return;

        // Ensure each target is only damaged once per swing
        if (currentItems[hand].hitTargets.Contains(targetNetObj)) return;
        currentItems[hand].hitTargets.Add(targetNetObj);

        if (targetNetObj.TryGetComponent<IHitable>(out var damageable))
        {
            damageable.OnHit();
        }
    }

    void OnDrawGizmos()
    {
        if(currentItems==null) return;
        for(int hand = 0; hand < currentItems.Length;hand++){
            if(currentItems[hand].currentItem==null) continue;
            Gizmos.color = Color.azure;

            var localPoints = currentItems[hand].currentItem.localPoints;
            int sign = hand<2 ? (hand*2)-1 : 1;
            
            for (int i = 0; i < localPoints.Length; i++)
            {
                Gizmos.DrawWireSphere(currentItems[hand].weaponSocket.TransformPoint(localPoints[i]*sign),
                                                currentItems[hand].currentItem.pointRadius);
            }
        }
    }


}
