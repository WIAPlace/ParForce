using Unity.Netcode;
using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [SerializeField] private GameObject myNetworkPrefab;
    public void Awake()
    {
        SpawnMyObject();
    }
    public void SpawnMyObject()
    {
        // Only the server/host can spawn NetworkObjects
        //if (!IsServer) return;

        // 1. Instantiate locally on the server
        GameObject instance = Instantiate(myNetworkPrefab, Vector3.zero, Quaternion.identity);

        // 2. Spawn across the network for all connected clients
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        netObj.Spawn();
    }
}
