using UnityEngine;
using HSM;
using System;
using System.Collections.Generic;
using Animancer;



#if UNITY_EDITOR
using UnityEditor;
#endif

public enum SlotType {Empty, Single, RightOnly, LeftOnly, TwoHanded}
public enum HandSlot {LeftHand, RightHand, TwoHanded}

public enum HeldType {Empty, Weapon, Shield, Magic}

public enum ActionType {None, Attack, Block}
public enum ActionOrder {None, Prim, Sec, PrimSec, SecPrim}



[CreateAssetMenu(menuName = "Item System/HeldItemData/_Basic")]
public class HeldItemData : ScriptableObject
{
    [Header("Network Identity")]
    public int serializedId;

    public ushort id // will be used for handeling calls over the network.
    {
        get => (ushort)Mathf.Clamp(serializedId, ushort.MinValue, ushort.MaxValue);
        set => serializedId = value;
    }

    [Header("Basics")]
    public string heldItemName;
    public GameObject visualPrefab;
    public SlotType slotType = SlotType.Empty;
    public HeldType heldType = HeldType.Empty;

    

    [Header("Actions")]
    public ItemAction[] actions;

    //[Header("Transform Positions")]
    //public string[] rayPointName; // points of the prefab that will be tracked during attack anims.
    public Vector3[] localPoints;
    public Vector3 localRot;
    public float pointRadius;

    [Header("Timer")]
    public float windUpTime;
    public float windDownTime;
    
    [Header("Data")]
    public float baseDamage;    

    [Header("Passive Animations")]
    public ClipTransition idle;
    //public AnimationClip walk;
    //public AnimationClip run;

    // Track states currently registered to the character's Animancer graph
    [NonSerialized] protected readonly List<AnimationClip> RegisteredClips = new List<AnimationClip>();

    // Preload animations, override in cases where held item data has diffrent information it will give
    public virtual void PreloadAnimations(AnimancerComponent animancer)
    {
        RegisteredClips.Clear();

        // Preload Logic
        foreach(ItemAction act in actions)
        {   // preload anims from each order
            if(act==null) continue;
            act.PreloadAnimations(animancer,RegisteredClips);
        }
    }

    // Safely clears out clips from Animancer's graph when this item is unequipped.
    public virtual void UnloadAnimations(AnimancerComponent animancer)
    {
        foreach (var clip in RegisteredClips)
        {
            if (clip != null)
            {
                animancer.States.Destroy(clip);
            }
        }
        RegisteredClips.Clear();
    }

    // Helper method for child classes to register safely
    protected void PoolClip(AnimancerComponent animancer, AnimationClip clip)
    {
        if (clip == null) return;
        animancer.States.GetOrCreate(clip);
        RegisteredClips.Add(clip);
    }

    // If this item has an action activly happening while the player presses an input it will go through this.
    public virtual int InputWhileActive(int key, bool isDown, int activeHand, Vector2Int activeAction, HeldItemData otherItem)
    {
        return -1; // simply let them do what they want.

        //Return Meaning
        // -1 Do whatever the other hand wants
        // 0 Block actions
        // 1 This Primary
        // 2 This Secondary
        // so on.
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        // Delay the check slightly to avoid serialization conflicts in the inspector
        EditorApplication.delayCall += _ValidateUniqueId;
    }

    private void _ValidateUniqueId()
    {
        if (this == null) return;

        // Find all ScriptableObjects of this type in the project
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(HeldItemData)}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            HeldItemData so = AssetDatabase.LoadAssetAtPath<HeldItemData>(path);

            // Skip checking against ourselves
            if (so != null && so != this && so.serializedId == this.id)
            {
                Debug.LogWarning($"Duplicate ID '{this.serializedId}' found on ScriptableObject '{this.name}' (collides with '{so.name}'). Please change the ID.", this);
                break;
            }
        }
    }
    #endif
}   

//[CreateAssetMenu(menuName = "Item System/Item Action")]
public abstract class ItemAction : ScriptableObject
{
    public ActionType actionType;
    public bool unStopable;

    //public 

    // Called the exact frame the button is pressed down
    public abstract void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID);
    
    // Called the frame the button is released
    public abstract void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID);
    
    // Executed on other clients and server via RPCs to display visuals/animations and perform certain logic, if its the server
    public abstract void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode,int actionDetails, bool isServer, bool isClient);

    protected bool CheckIfHandsAreCorrect(HandSlot hand, PlayerContext ctx)
    {
        switch (hand)
        {
            case HandSlot.TwoHanded when ctx.Hand[0] == null && ctx.Hand[1] == null:
                return false;
            case HandSlot.LeftHand when ctx.Hand[0] == null:
                return false;
            case HandSlot.RightHand when ctx.Hand[1] == null:
                return false;
                
            default:
                break;
        }
        return true;
    }

    protected int CheckHandUsed(HandSlot hand, PlayerContext ctx)
    {
        switch (hand)
        {
            case HandSlot.TwoHanded: 
                return ctx.Hand[0] != null ? 0 : 1; 
            case HandSlot.LeftHand when ctx.Hand[0] != null:
                return 0;
            case HandSlot.RightHand when ctx.Hand[1] != null:
                return 1;
            default:
                return -1;
        }
    }

    public abstract void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips);

    // Helper method for child classes to register safely
    protected void PoolClip(AnimancerComponent animancer, AnimationClip clip, List<AnimationClip> RegisteredClips)
    {
        if (clip == null) return;
        animancer.States.GetOrCreate(clip);
        RegisteredClips.Add(clip);
    }
}
