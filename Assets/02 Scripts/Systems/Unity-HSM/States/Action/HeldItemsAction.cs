using UnityEngine;
using HSM;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Build.Pipeline.Injector;

public class HeldItemsAction : State
{
    readonly PlayerContext ctx;
    Vector2Int[] requestedAction;

    public HeldItemsAction(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
        requestedAction = new Vector2Int[ctx.lastAction.Length];
    }

    protected override State GetTransition()
    {
        return base.GetTransition();
    }

    protected override void OnEnter()
    {
        base.OnEnter();
    }

    protected override void OnExit()
    {
        base.OnExit();
    }

    protected override void OnUpdate(float deltaTime)
    {
        // check if there is anything in the Input Buffers of either Hand
        // go through each of them.
        // 0 is left, 1 is right, and so on...
        for(int i = 0; i < ctx.actionInputs.Length; i++) /////////////////////////////////////////////////////////////////////////// Inputs
        {
            
            if (ctx.actionInputs[i].TryConsume(ctx.currentTick, out bool isDown))
            {
                // evaluate what this key being down means then store that inside of a struct 
                // then store that struct in a circular buffer 
                // so we can see what the last action taken was for future evaluations

                int offHand = -1;// -1 means we arnt dealing with left or right hand. use under specific circumstances.
                if(i<2) offHand = (i+1)%2; // should set 1 to 0 / 0 to 1 to get the off hand if this is primary.


                // If Off Hand Item Is Active ////////////////////////////////////////////////////////////////////////////////
                HeldItemData offHandItem = ctx.Hand[offHand];
                if(ctx.lastAction[offHand].x >= 1 && offHandItem != null && offHandItem.heldType != HeldType.Empty)
                {
                    // Get 'opinion' from active item.
                    int order = offHandItem.InputWhileActive(i,isDown,offHand,ctx.lastAction[offHand].x,ctx.Hand[i]);
                    if(order == 0) 
                    {
                        // Block input.
                        continue;
                    }
                    else if(order > 0)
                    {
                        int keyState = isDown ? 0 : 1; // 0 is on start, 1 is on release
                        // Have the off Hand perform an action related to the order given, 1 is primary, 2 is secondary. 
                        
                        requestedAction[offHand] = new Vector2Int(order,keyState); 
                        continue;
                    }
                    // if it is less than zero then continue. outside of the if statement.
                }

                if(ctx.Hand[i]!=null && ctx.Hand[i].heldType!=HeldType.Empty) //////////////////// If Hand actualy has something in it.
                {
                    // If Item is Already Active ////////////////////////////////////////////////////////////////////////////////////
                    if (ctx.lastAction[i].x > 0) // if current item is active
                    {
                        if (!isDown) // Release
                        {
                            // ctx.lastAction[i].x = action Order, OnRelease = 1.
                            requestedAction[i] = new Vector2Int(ctx.lastAction[i].x,1); 
                            continue;
                        }
                        else
                        { // I don't think this should happen. 
                            Debug.Log("Item is active, when it probably should not be");
                        }
                    }

                    // If Nothing is Active /////////////////////////////////////////////////////////////////////////////////////////
                    if (isDown) // I dont think anything should be possible on release while inactive.
                    {
                        // Decide what action should be taken here and On Start.
                        continue;
                    }
                }
                
            }
        }

        for(int i = 0; i<ctx.lastAction.Length; i++) ///////////////////////////////////////////////////////////////////////////////// Requested Actions
        {
            if (ctx.lastAction[i] != requestedAction[i] && !ctx.handIsBusy[i])
            { // if something has changed lets reflect it, once the hand is not busy.
                ctx.lastAction[i] = requestedAction[i];
                switch (requestedAction[i].y)
                {   // on Start
                    case 0:
                        // action order based off of x-1

                        continue;
                    case 1:
                        // action order based off of x-1

                        continue;
                    default:
                        Debug.Log("Current Action's Key State is outside of Range");
                        break;
                }
            }
        }
    }
}
