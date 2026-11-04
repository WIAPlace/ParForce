using HSM;
using Unity.Netcode;
using UnityEngine;

public class NetworkItemSync : NetworkBehaviour
{
    private PlayerContext ctx;
    private EquipmentManager localEquipment;
    private Animator animator;

    public void OnStart(PlayerContext ctx)
    {
        this.ctx = ctx;
        localEquipment = ctx.equipmentManager;
        animator = ctx.anim;
    }

    /// <summary>
    /// 1. The local player calls this from their input script or ScriptableObject action.
    /// </summary>
    public void BroadcastAction(int itemID, HandSlot slot, int actionTypeCode)
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

    /// <summary>
    /// 2. The Host/Server receives the input execution request from a client.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExecuteActionServerRpc(int itemID, HandSlot slot, int actionTypeCode)
    {
        // Host immediately forwards this down to all other players in the session
        ExecuteActionClientRpc(itemID, slot, actionTypeCode);
    }

    /// <summary>
    /// 3. Every connected machine executes this code to update puppet visuals.
    /// </summary>
    [ClientRpc]
    private void ExecuteActionClientRpc(int itemID, HandSlot slot, int actionTypeCode)
    {
        // Safety First: Avoid running this on the client who originally swung the sword.
        // Their game already played the animation instantly on frame one!
        if (IsOwner) return; 

        // CRITICAL DATA LINK: Look up the structural asset locally using the network integer ID
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID);
        
        if (item != null && item.primaryAction is ItemAction netAction)
        {
            // Run the non-laggy, purely visual side of the script for this remote player character
            netAction.OnRemoteExecute(slot, ctx ,actionTypeCode);
        }
    }

    /// <summary>
    /// Casual client-authoritative damage processor running straight through the host.
    /// </summary>
    [ServerRpc]
    public void ApplyDamageServerRpc(NetworkObjectReference targetNetObj, float damage)
    {
        if (targetNetObj.TryGet(out NetworkObject netObj))
        {
            // Host applies the health deduction to the target (enemy or player)
            // e.g., netObj.GetComponent<Health>().ModifyHealth(-damage);
            Debug.Log($"[Host] Applied {damage} damage to network entity: {netObj.name}");
        }
    }
}
