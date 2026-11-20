using UnityEngine;
using HSM;

public class HeldItemsAction : State
{
    readonly PlayerContext ctx;
    //Vector2Int[] ctx.requestedAction;//e
    

    public HeldItemsAction(StateMachine m, State parent, PlayerContext ctx) : base(m, parent) {
        this.ctx = ctx;
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
        //// Check For any new Inputs //////////////////////////////////////////////////////////////////////////////////////////////////////
        for(int i = 0; i < ctx.actionInputs.Length; i++) /////////////////////////////////////////////////////////////////////////// Inputs
        {
            if(ctx.actionInputs == null || ctx.actionInputs[i] == null) continue;
            //// Check if there is a new input in the Buffer ///////////////////////////////////////////////////////////////////////////////
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
                    int order = offHandItem.OffInputWhileActive(i,isDown,offHand,ctx.lastAction[offHand],ctx.Hand[i]);
                    if(order == 0) 
                    {
                        // Block input.
                        continue;
                    }
                    else if(order > 0)
                    {
                        int keyState = isDown ? 0 : 1; // 0 is on start, 1 is on release
                        // Have the off Hand perform an action related to the order given, 1 is primary, 2 is secondary. 
                        
                        ctx.requestedAction[offHand].Add(new Vector2Int(order,keyState),ctx.currentTick); 
                        continue;
                    }
                    // if it is less than zero then continue. outside of the if statement.
                }

                // If Hand actualy has something in it. ////////////////////////////////////////////////////////////////////////////////
                if(ctx.Hand[i]!=null && ctx.Hand[i].heldType!=HeldType.Empty) 
                {
                    // If Hand has something in it///////////////////////////////////
                    if(ctx.lastAction[i].x >= 1)
                    {
                        int order = ctx.Hand[i].MainInputWhileActive(isDown,i,ctx.lastAction[i]);
                        if(order == 0) continue;
                        else if(order > 0)
                        {
                            int keyState = isDown ? 0 : 1; // 0 is on start, 1 is on release
                            // Have the off Hand perform an action related to the order given, 1 is primary, 2 is secondary. 
                            
                            ctx.requestedAction[i].Add(new Vector2Int(order,keyState),ctx.currentTick); 
                            continue;
                        }
                    }

                    if (isDown) // if Key is Down
                    {
                        // Decide what action should be taken here and On Start.
                        ctx.requestedAction[i].Add(new Vector2Int(1,0),ctx.currentTick); // Primary, on start
                        continue;
                    }
                    else // If Key is not Down
                    {
                        ctx.requestedAction[i].Add(new Vector2Int(1,1),ctx.currentTick); // Primary, on Release
                        continue;
                    }
                }

                // If No Item is Active, but only Other Hand Has Item in it /////////////////////////////////////////////////////////////
                if(i<2 && offHandItem != null && offHandItem.heldType != HeldType.Empty && offHandItem.actions.Length >= 2) // <2 cause this will only work for left/right hand
                {
                    if (isDown) // if Key is Down
                    {
                        // Decide what action should be taken here and On Start.
                        ctx.requestedAction[offHand].Add(new Vector2Int(2,0),ctx.currentTick); // Primary, on start
                        continue;
                    }
                    else // If Key is not Down
                    {
                        ctx.requestedAction[offHand].Add(new Vector2Int(2,1),ctx.currentTick); // Primary, on Release
                        continue;
                    }
                }
            }
        }
        
        //// Check if there is a new Requested Action //////////////////////////////////////////////////////////////////////////////////////////////////
        for(int i = 0; i<ctx.lastAction.Length; i++) ///////////////////////////////////////////////////////////////////////////////// Requested Actions
        {
            //Debug.Log(ctx.handIsBusy[i]);
            if(ctx.handIsBusy[i] || !ctx.requestedAction[i].Check()) continue; // null catch

            if (ctx.requestedAction[i].TryConsume(ctx.currentTick, out ctx.lastAction[i]))
            { // if something has changed lets reflect it, once the hand is not busy.

                int requestedOrder = ctx.lastAction[i].x-1;
                ushort id = ctx.Hand[i].id;

                switch (ctx.lastAction[i].y)
                {   // on Start
                    case 0:
                        // action order based off of x-1
                        //Debug.Log((HandSlot)i + " started their " + (ActionOrder)(requestedOrder+1) + " Action on Tick = " +ctx.currentTick);
                        ctx.Hand[i].actions[requestedOrder].OnLocalStart((HandSlot)i,ctx,id);
                        continue;
                    case 1:
                        // action order based off of x-1
                        //Debug.Log((HandSlot)i + " released their " + (ActionOrder)(requestedOrder+1) + " Action on Tick = " +ctx.currentTick);
                        ctx.Hand[i].actions[requestedOrder].OnLocalRelease((HandSlot)i,ctx,id);
                        continue;
                    default:
                        Debug.Log("Current Action's Key State is outside of Range");
                        break;
                }
            }
        }
    }
}
