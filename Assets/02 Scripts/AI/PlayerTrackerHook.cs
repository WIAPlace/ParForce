using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to player Prefab.
/// When the player spawns it will add itself to the player position tracker, 
/// to be used by ai for world position and the like
/// </summary>
public class PlayerTrackerHook : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (PlayerPositionTracker.Instance != null)
        {
            PlayerPositionTracker.Instance.RegisterPlayer(OwnerClientId, transform);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (PlayerPositionTracker.Instance != null)
        {
            PlayerPositionTracker.Instance.UnregisterPlayer(OwnerClientId);
        }
        base.OnNetworkDespawn();
    }
}
