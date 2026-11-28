using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player Position Tracker will be used by GOAP AI agents to get information about where the players are in relation to them 
/// </summary>
public class PlayerPositionTracker : MonoBehaviour
{
    public static PlayerPositionTracker Instance { get; private set; }

    // Maps each player's ClientId to their active Transform
    public Dictionary<ulong, Transform> PlayerTransforms { get; private set; } = new Dictionary<ulong, Transform>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RegisterPlayer(ulong clientId, Transform playerTransform)
    {
        PlayerTransforms[clientId] = playerTransform;
    }

    public void UnregisterPlayer(ulong clientId)
    {
        if (PlayerTransforms.ContainsKey(clientId))
        {
            PlayerTransforms.Remove(clientId);
        }
    }

    // Example helper to get all current Vector3 positions
    public Dictionary<ulong, Vector3> GetAllPlayerPositions()
    {
        var positions = new Dictionary<ulong, Vector3>();
        foreach (var kvp in PlayerTransforms)
        {
            if (kvp.Value != null)
            {
                positions[kvp.Key] = kvp.Value.position;
            }
        }
        return positions;
    }
}
