using Animancer;
using Unity.Netcode;
using UnityEngine;

public class EquipmentManager : NetworkBehaviour
{
    public Transform leftHandBone;
    public Transform rightHandBone;

    // NetworkVariables sync the Item IDs across the network automatically
    public NetworkVariable<ushort> leftHandItemID = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<ushort> rightHandItemID = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private GameObject currentLeftModel;
    private GameObject currentRightModel;

    // list of active items. basicly left and right hand
    public HeldItemData[] activeItems {get;private set;} = new HeldItemData[2];

    //[HideInInspector] 
    public AnimancerComponent animancer;

    public override void OnNetworkSpawn()
    {
        // Listen for changes so we can spawn/despawn visual prefabs locally
        leftHandItemID.OnValueChanged += OnLeftItemChanged;
        rightHandItemID.OnValueChanged += OnRightItemChanged;

        // Initialize Item if spawning late
        UpdateItem(leftHandItemID.Value, leftHandBone, 0, ref currentLeftModel);
        UpdateItem(rightHandItemID.Value, rightHandBone, 1, ref currentRightModel);
    }

    // Called on the server to change equipment
    [ServerRpc]
    public void RequestEquipItemServerRpc(ushort itemID, HandSlot slot)
    {
        if (slot == HandSlot.LeftHand) leftHandItemID.Value = itemID;
        if (slot == HandSlot.RightHand) rightHandItemID.Value = itemID;
    }

    private void OnLeftItemChanged(ushort oldID, ushort newID) => UpdateItem(newID, leftHandBone, 0, ref currentLeftModel);
    private void OnRightItemChanged(ushort oldID, ushort newID) => UpdateItem(newID, rightHandBone, 1, ref currentRightModel);

    private void UpdateItem(ushort itemID, Transform handBone, int index, ref GameObject currentModel)
    {
        if (currentModel != null) Destroy(currentModel);
        if (itemID == 0) return;

        // Look up the ItemData asset locally using the Network ID
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID); 
        if(item!=null){
            
            activeItems[index] = item;

            LocalEquipAndPool(itemID,index);

            if (item.visualPrefab != null)
            {
                currentModel = Instantiate(item.visualPrefab, handBone);
                currentModel.transform.localPosition = Vector3.zero;
                currentModel.transform.localRotation = Quaternion.identity;
            }

            if (IsOwner)
            {
                NotifyServerEquipmentChangedServerRpc(itemID,index);
            }
        }
    }

    [ServerRpc]
    private void NotifyServerEquipmentChangedServerRpc(ushort itemID,int index)
    {
        // Replicate equipment change to all proxy observers
        ReplicateEquipmentClientRpc(itemID, index);
    }

    [ClientRpc]
    private void ReplicateEquipmentClientRpc(ushort itemID,int index)
    {
        // Non-owners (proxies) capture the message and configure their graphs
        if (!IsOwner)
        {
            LocalEquipAndPool(itemID, index);
        }
    }

    private void LocalEquipAndPool(ushort itemID, int index)
    {
        if (activeItems[index] != null) activeItems[index].UnloadAnimations(animancer);

        activeItems[index] = HeldItemDatabase.Instance.GetItemByID(itemID);
        
        if (activeItems[index] != null)
        {
            activeItems[index].PreloadAnimations(animancer);
        }
    }
}
