using System.Collections.Generic;
using Animancer;
using HSM;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(menuName = "Item System/Item Action/MeleeAction")]
public class MeleeAction : ItemAction
{
    //private static readonly int TriggerHash = Animator.StringToHash("Swing");
    public string actionLable = "SwingActive";
    public string progressLable = "Swing";
    public int[] chainLength;
    [Range(0,1)]public float[] chainWindow;
    

    public override void OnLocalStart(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        if(!CheckIfHandsAreCorrect(hand,ctx)) return; // check to see if there is anything for the hands to do. 
        ctx.raid.chainLength[(int)hand] = chainLength; // set the chain length of both the light and heavy attack.
        ctx.raid.chainWindow[(int)hand] = chainWindow; // if there is no chain windo make it 0.
        ctx.raid.unStopable[(int)hand] = unStopable; // throws if this action is able to be relased to be stopped.

        Animator anim = ctx.anim;
        //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
        string side = hand.ToString(); // we can come back to this to figure out what is the best choice.

        ctx.raid.currentProgressHash = ActionAnimationHasher.GetTriggerHash(side+progressLable);
        
        // Set up trigger for local anim
        string animTrigger = side + actionLable;
        //Debug.Log(animTrigger);

        int hash = ActionAnimationHasher.GetTriggerHash(animTrigger);

        // Set variable inside of RAID.
        ctx.raid.currentActionHash = hash;

        // Broadcast the exact combo index to everyone else (using it as the actionTypeCode)
        int currentComboIndex=0;
        ctx.BroadcastAction(itemID,hand,currentComboIndex);

        //PerformHitCheck(user, ctx, damageToDeal, hand);
    }

    public override void OnLocalRelease(HandSlot hand, PlayerContext ctx, ushort itemID)
    {
        //throw new System.NotImplementedException();
    }

    public override void OnRemoteExecute(HandSlot hand, PlayerContext ctx, int actionTypeCode)
    {
        // Remote puppets receive the raw combo index as the actionTypeCode
        int remoteComboIndex = actionTypeCode;

        // Safety check to ensure array indices match across build clients
        //if (remoteComboIndex >= 0 && remoteComboIndex < comboAnimationTriggers.Length)
        //{
            Animator anim = ctx.anim;
            //string side = (hand == HandSlot.LeftHand) ? "Left" : "Right";
            string side = hand.ToString(); // we can come back to this to figure out what is the best choice.
            string dynamicTrigger = side + "Swing";
            
            anim.SetTrigger(dynamicTrigger);
        //}
    }

    public override void PreloadAnimations(AnimancerComponent animancer, List<AnimationClip> RegisteredClips)
    {
        throw new System.NotImplementedException();
    }

    /*
    private void PerformHitCheck(GameObject user, PlayerContext ctx, float damageValue, HandSlot hand)
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, attackRange))
        {
            if (hit.collider.TryGetComponent<NetworkObject>(out var netObj))
            {
                ctx.ApplyDamageServerRpc(netObj, damageValue);
            }
        }
    }

    using System.Collections.Generic;
    using UnityEngine;

    public class ChainAttackCombo : MonoBehaviour
    {
        private Animator animator;
        
        // Cache dictionary: Stores the attack number as a key, and its hash as the value
        private static readonly Dictionary<int, int> AttackHashCache = new Dictionary<int, int>();

        void Start()
        {
            animator = GetComponent<Animator>();
        }

        public void TriggerChainAttack(int attackIndex)
        {
            // 1. Look up the hash in our dictionary first
            if (!AttackHashCache.TryGetValue(attackIndex, out int targetHash))
            {
                // 2. If it's not there, build the string (e.g., "Attack_3") and hash it
                string parameterName = $"Attack_{attackIndex}";
                targetHash = Animator.StringToHash(parameterName);
                
                // 3. Save it so we never have to build or hash this string again
                AttackHashCache.Add(attackIndex, targetHash);
                Debug.Log($"Generated and cached new hash for: {parameterName}");
            }

            // 4. Fire the trigger safely using the cached integer
            animator.SetTrigger(targetHash);
        }
    }

    */
}
