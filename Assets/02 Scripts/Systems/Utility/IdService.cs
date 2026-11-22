using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class IdService : NetworkBehaviour
{
    public static IdService Instance { get; private set; }

    // Server-side counter for generated IDs
    private uint _serverNextId = 1;

    // Client-side tracking to match responses to the exact caller
    private uint _localRequestCounter = 0;
    private readonly Dictionary<uint, TaskCompletionSource<uint>> _pendingRequests = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Static entry point callable from anywhere in your codebase.
    /// </summary>
    public static async Task<uint> GetNewIdAsync()
    {
        if (Instance == null || !Instance.IsSpawned)
        {
            
            Debug.LogError($"IdService failed! Instance is null: {Instance == null} | " +
                        $"IsListening: {NetworkManager.Singleton?.IsListening} | " +
                        $"IsHost: {NetworkManager.Singleton?.IsHost} | " +
                        $"SceneManagement: {NetworkManager.Singleton?.NetworkConfig.EnableSceneManagement} | " );//+
                        //$"Hash: {Instance?.NetworkObject?.GlobalObjectIdHash}");
            return 0;
        }

        // If this instance is the Host/Server, bypass the network round-trip
        if (Instance.IsServer)
        {
            return Instance._serverNextId++;
        }

        // Create a unique local token so multiple simultaneous requests don't collide
        uint requestToken = Instance._localRequestCounter++;
        var tcs = new TaskCompletionSource<uint>();
        Instance._pendingRequests[requestToken] = tcs;

        // Send the request to the server
        Instance.RequestIdServerRpc(requestToken);

        // Wait until the server's ClientRpc resolves this TaskCompletionSource
        return await tcs.Task;
    }

    [Rpc(SendTo.Server)]
    private void RequestIdServerRpc(uint requestToken, RpcParams rpcParams = default)
    {
        uint assignedId = _serverNextId++;
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        ReceiveIdClientRpc(requestToken, assignedId, RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ReceiveIdClientRpc(uint requestToken, uint assignedId, RpcParams rpcParams = default)
    {
        if (_pendingRequests.TryGetValue(requestToken, out var tcs))
        {
            _pendingRequests.Remove(requestToken);
            tcs.SetResult(assignedId);
        }
    }

    public override void OnNetworkDespawn()
    {
        // Cancel any lingering tasks if the client disconnects mid-request
        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }
}
