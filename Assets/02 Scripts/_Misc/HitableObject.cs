using Unity.Netcode;
using UnityEngine;

public class HitableObject : NetworkBehaviour, IHitable
{
    public void OnHit()
    {
        Debug.Log("Hit");
    }
}
