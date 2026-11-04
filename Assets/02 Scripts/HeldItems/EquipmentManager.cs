using Unity.Netcode;
using UnityEngine;

public class EquipmentManager : NetworkBehaviour
{
    public Transform leftHandBone;
    public Transform rightHandBone;

    // NetworkVariables sync the Item IDs across the network automatically
    public NetworkVariable<int> leftHandItemID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> rightHandItemID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GameObject currentLeftModel;
    private GameObject currentRightModel;

    public override void OnNetworkSpawn()
    {
        // Listen for changes so we can spawn/despawn visual prefabs locally
        leftHandItemID.OnValueChanged += OnLeftItemChanged;
        rightHandItemID.OnValueChanged += OnRightItemChanged;

        // Initialize visuals if spawning late
        UpdateVisuals(leftHandItemID.Value, leftHandBone, ref currentLeftModel);
        UpdateVisuals(rightHandItemID.Value, rightHandBone, ref currentRightModel);
    }

    // Called on the server to change equipment
    [ServerRpc]
    public void RequestEquipItemServerRpc(int itemID, HandSlot slot)
    {
        if (slot == HandSlot.LeftHand) leftHandItemID.Value = itemID;
        if (slot == HandSlot.RightHand) rightHandItemID.Value = itemID;
    }

    private void OnLeftItemChanged(int oldID, int newID) => UpdateVisuals(newID, leftHandBone, ref currentLeftModel);
    private void OnRightItemChanged(int oldID, int newID) => UpdateVisuals(newID, rightHandBone, ref currentRightModel);

    private void UpdateVisuals(int itemID, Transform handBone, ref GameObject currentModel)
    {
        if (currentModel != null) Destroy(currentModel);
        if (itemID == -1) return;

        // Look up the ItemData asset locally using the Network ID
        HeldItemData item = HeldItemDatabase.Instance.GetItemByID(itemID); 
        if (item != null && item.visualPrefab != null)
        {
            currentModel = Instantiate(item.visualPrefab, handBone);
            currentModel.transform.localPosition = Vector3.zero;
            currentModel.transform.localRotation = Quaternion.identity;
        }
    }
}
