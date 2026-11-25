using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Shared interaction context for all inventory panels opened together.
    /// Owns drag orientation and live placement validation for every panel.
    /// </summary>
    public sealed class InventoryPanelSession : MonoBehaviour
    {
        private readonly List<InventoryPanelView> panels = new List<InventoryPanelView>();

        public InventoryDragCoordinator DragCoordinator { get; } = new InventoryDragCoordinator();
        public InventoryTransferService TransferService { get; } = new InventoryTransferService();
        public InventoryQuickMoveService QuickMoveService { get; } = new InventoryQuickMoveService();
        public InventoryItemActionService ItemActionService { get; } = new InventoryItemActionService();
        public event Action<InventoryTransferResult> TransferCompleted;
        public event Action<InventoryQuickMoveResult> QuickMoveCompleted;
        public event Action<InventoryItemActionExecution> ItemActionExecuted;

        /// <summary>Raised after Q removes the held item from its source inventory.
        /// Subscribe a world-item handler to spawn/throw the item. If nobody is subscribed,
        /// the item is not removed, preventing accidental loss.</summary>
        public event Action<Item, InventoryPanelView> ItemDropped;

        public bool IsDragging => DragCoordinator.IsDragging;
        private InventoryPanelView sourceView;
        private int draggedItemRotationQuarterTurns;
        private Vector2 lastScreenPosition;
        private Item draggedItem;
        private IInventoryModel dragSourceModel;
        private SerializableGuid visualSourceItemId;
        private SerializableGuid splitOriginalItemId;
        private IInventoryModel splitOriginalModel;
        private bool isSplitDrag;
        private bool ignoreNextRightRelease;
        private InventoryPanelView hoveredPanelDuringDrag;

        public void Register(InventoryPanelView view)
        {
            if (view != null && !panels.Contains(view))
                panels.Add(view);
        }

        public void Unregister(InventoryPanelView view)
        {
            if (view == null) return;
            panels.Remove(view);
            if (ReferenceEquals(sourceView, view)) CancelDrag();
        }

        /// <summary>Cancels an active drag when its source panel is closed or hidden.</summary>
        public void CancelDragFrom(InventoryPanelView view)
        {
            if (view != null && ReferenceEquals(sourceView, view))
                CancelDrag();
        }

        /// <summary>Moves a panel to the top of the shared canvas without changing its position.</summary>
        public void BringToFront(InventoryPanelView view)
        {
            if (view == null || !panels.Contains(view)) return;
            panels.Remove(view);
            panels.Add(view);
            view.BringPanelToFront();
        }

        /// <summary>Shift-click destination selection: highest priority first, with the source panel excluded.</summary>
        public InventoryQuickMoveResult QuickMove(InventoryPanelView source, SerializableGuid itemInstanceId)
        {
            if (source == null || source.CurrentModel == null)
                return new InventoryQuickMoveResult(0, 0, "The source inventory is unavailable.");

            List<IInventoryModel> destinations = new List<IInventoryModel>();
            HashSet<SerializableGuid> addedIds = new HashSet<SerializableGuid>();
            foreach (InventoryPanelView panel in panels)
            {
                if (panel == null || ReferenceEquals(panel, source) || panel.CurrentModel == null ||
                    panel.CurrentModel.Id == source.CurrentModel.Id || !addedIds.Add(panel.CurrentModel.Id))
                    continue;
                destinations.Add(panel.CurrentModel);
            }

            destinations = destinations
                .OrderByDescending(model => GetPanelPriority(model))
                .ToList();
            InventoryQuickMoveResult result = QuickMoveService.QuickMove(source.CurrentModel, itemInstanceId, destinations);
            QuickMoveCompleted?.Invoke(result);
            if (!result.Success) Debug.Log(result.Message, this);
            return result;
        }

        public IReadOnlyList<InventoryItemAction> GetItemActions(InventoryPanelView source, SerializableGuid itemId)
        {
            return source != null
                ? ItemActionService.GetConfiguredActions(source.CurrentModel, itemId)
                : Array.Empty<InventoryItemAction>();
        }

        public bool CanExecuteItemAction(InventoryPanelView source, SerializableGuid itemId,
            InventoryItemAction action, out string reason)
        {
            if (source == null)
            {
                reason = "The inventory panel is unavailable.";
                return false;
            }
            return ItemActionService.CanExecute(source.CurrentModel, itemId, action, out reason);
        }

        public InventoryItemActionExecution ExecuteItemAction(InventoryPanelView source,
            SerializableGuid itemId, InventoryItemAction action)
        {
            InventoryItemActionExecution execution = ItemActionService.Execute(
                source != null ? source.CurrentModel : null, itemId, action);
            ItemActionExecuted?.Invoke(execution);
            if (execution.Result != null && !execution.Result.Success && !string.IsNullOrEmpty(execution.Result.Message))
                Debug.LogWarning(execution.Result.Message, this);
            return execution;
        }

        private int GetPanelPriority(IInventoryModel model)
        {
            foreach (InventoryPanelView panel in panels)
                if (panel != null && panel.CurrentModel != null && panel.CurrentModel.Id == model.Id)
                    return panel.QuickMovePriority;
            return 0;
        }

        public bool BeginDrag(InventoryPanelView source, SerializableGuid itemInstanceId, int pointerId)
        {
            if (source == null || source.CurrentModel == null || IsDragging)
                return false;
            if (!source.CurrentModel.TryGetItem(itemInstanceId, out Item item))
                return false;
            if (!DragCoordinator.TryBeginDrag(source.CurrentModel.Id, itemInstanceId))
                return false;

            sourceView = source;
            draggedItem = item;
            dragSourceModel = source.CurrentModel;
            visualSourceItemId = itemInstanceId;
            isSplitDrag = false;
            splitOriginalItemId = SerializableGuid.Empty;
            splitOriginalModel = null;
            ignoreNextRightRelease = false;
            source.SetItemDragging(itemInstanceId, true);
            BringToFront(source);
            draggedItemRotationQuarterTurns = source.CurrentModel is IGridInventoryModel sourceGrid &&
                                 sourceGrid.TryGetPlacement(itemInstanceId, out GridItemPlacement placement)
                ? placement.RotationQuarterTurns : 0;
            source.FocusForDrag();
            source.CapturePointer(pointerId);
            return true;
        }

        /// <summary>Right-click pickup: splits the larger half of a stack into a temporary drag payload.</summary>
        public bool BeginSplitDrag(InventoryPanelView source, SerializableGuid itemInstanceId, int pointerId)
        {
            if (source == null || source.CurrentModel == null || IsDragging ||
                !source.CurrentModel.TryGetItem(itemInstanceId, out Item original) || original == null ||
                original.details == null ||
                original.details.maxStack <= 1 || original.quantity <= 1)
                return false;

            int originalQuantity = original.quantity;
            int heldQuantity = (originalQuantity + 1) / 2;
            int remainingQuantity = originalQuantity - heldQuantity;
            if (!source.CurrentModel.SetQuantity(original.Id, remainingQuantity).Success)
                return false;

            Item splitItem = new Item(original.details, heldQuantity);
            CollectionInventoryModel temporarySource = new CollectionInventoryModel(
                SerializableGuid.NewGuid(), "Held Stack", 1, new[] { splitItem });
            if (!DragCoordinator.TryBeginDrag(temporarySource.Id, splitItem.Id))
            {
                source.CurrentModel.SetQuantity(original.Id, originalQuantity);
                return false;
            }

            sourceView = source;
            draggedItem = splitItem;
            dragSourceModel = temporarySource;
            visualSourceItemId = original.Id;
            splitOriginalItemId = original.Id;
            splitOriginalModel = source.CurrentModel;
            isSplitDrag = true;
            ignoreNextRightRelease = true;
            // A split leaves a real stack in its original slot, so keep that stack fully visible.
            // Only a full-stack drag should fade the source item.
            BringToFront(source);
            draggedItemRotationQuarterTurns = source.CurrentModel is IGridInventoryModel sourceGrid &&
                sourceGrid.TryGetPlacement(original.Id, out GridItemPlacement placement)
                ? placement.RotationQuarterTurns : 0;
            source.FocusForDrag();
            source.CapturePointer(pointerId);
            return true;
        }

        /// <summary>Consumes the pointer-up generated by the right-click that began a split drag.</summary>
        public bool ConsumeSplitInitiationRelease(int button)
        {
            if (button != 1 || !ignoreNextRightRelease) return false;
            ignoreNextRightRelease = false;
            return true;
        }

        /// <summary>Places one unit from the held stack into the slot/cell under a right-click release.</summary>
        public bool PlaceOneAt(Vector2 screenPosition)
        {
            if (!IsDragging || dragSourceModel == null || draggedItem == null || draggedItem.quantity < 1 ||
                draggedItem.details == null)
                return false;

            InventoryPanelView destination = FindPanelAt(screenPosition);
            if (destination == null || destination.CurrentModel == null ||
                destination.CurrentModel is IEquipmentInventoryModel) return false;

            int pointerSlot = destination.GetSlotIndexAt(screenPosition);
            if (pointerSlot < 0) return false;

            Item target = null;
            IGridInventoryModel grid = destination.CurrentModel as IGridInventoryModel;
            if (grid != null)
            {
                int pointerX = pointerSlot % grid.Width;
                int pointerY = pointerSlot / grid.Width;
                grid.TryGetItemAtCell(pointerX, pointerY, out target);
            }
            else if (destination.CurrentModel is ISlotInventoryModel slots)
            {
                slots.TryGetItemAt(pointerSlot, out target);
            }
            else return false;

            // Occupied cells are stack targets. Keep the pointer cell for this check;
            // pivot adjustment is only relevant when placing into empty grid space.
            if (target != null)
            {
                if (target.Id == draggedItem.Id || !QuickMoveService.CanMerge(draggedItem, target)) return false;

                int oldSourceQuantity = draggedItem.quantity;
                int oldTargetQuantity = target.quantity;
                if (!destination.CurrentModel.SetQuantity(target.Id, oldTargetQuantity + 1).Success)
                    return false;

                bool sourceEmptied;
                if (!ConsumeOneHeldUnit(oldSourceQuantity, out sourceEmptied))
                {
                    destination.CurrentModel.SetQuantity(target.Id, oldTargetQuantity);
                    return false;
                }

                if (sourceEmptied) CompleteConsumedSplitDrag();
                else UpdateDragTarget(screenPosition);
                return true;
            }

            Item oneItem = new Item(draggedItem.details, 1);
            int destinationSlot = pointerSlot;
            if (grid != null)
            {
                // Match normal drag placement: the cell under the pointer corresponds
                // to the item's DragPivot, not necessarily its top-left cell.
                destinationSlot = GetPivotAdjustedSlot(pointerSlot, grid.Width, grid.Height,
                    draggedItemRotationQuarterTurns, oneItem);
                if (destinationSlot < 0) return false;

                int x = destinationSlot % grid.Width;
                int y = destinationSlot / grid.Width;
                if (!grid.CanPlaceAt(oneItem, x, y, draggedItemRotationQuarterTurns, out _))
                    return false;
            }
            else if (!((ISlotInventoryModel)destination.CurrentModel).CanAcceptAt(oneItem, destinationSlot, out _))
            {
                return false;
            }

            int previousQuantity = draggedItem.quantity;
            bool emptied = false;
            if (!ConsumeOneHeldUnit(previousQuantity, out emptied)) return false;

            InventoryOperationResult addResult = grid != null
                ? grid.AddAt(oneItem, destinationSlot % grid.Width, destinationSlot / grid.Width,
                    draggedItemRotationQuarterTurns)
                : ((ISlotInventoryModel)destination.CurrentModel).AddAt(oneItem, destinationSlot);

            if (!addResult.Success)
            {
                RestoreHeldStackAfterFailedPlacement(previousQuantity, emptied);
                return false;
            }

            if (emptied) CompleteConsumedSplitDrag();
            else UpdateDragTarget(screenPosition);
            return true;
        }

        /// <summary>Consumes one unit from the temporary stack, removing it when the last unit is used.</summary>
        private bool ConsumeOneHeldUnit(int previousQuantity, out bool emptied)
        {
            emptied = previousQuantity == 1;
            InventoryOperationResult result = emptied
                ? dragSourceModel.Remove(draggedItem.Id)
                : dragSourceModel.SetQuantity(draggedItem.Id, previousQuantity - 1);
            return result != null && result.Success;
        }

        /// <summary>Restores the held stack if adding the unit to its destination fails.</summary>
        private bool RestoreHeldStackAfterFailedPlacement(int previousQuantity, bool wasEmptied)
        {
            InventoryOperationResult result = wasEmptied
                ? dragSourceModel.Add(draggedItem)
                : dragSourceModel.SetQuantity(draggedItem.Id, previousQuantity);
            if (result == null || !result.Success)
            {
                Debug.LogError("Could not restore the held stack after a failed right-click placement: " +
                    (result != null ? result.Failure.ToString() : "InvalidOperation") + ".", this);
                return false;
            }
            return true;
        }

        /// <summary>Ends a split drag after its final held unit has been successfully placed.</summary>
        private void CompleteConsumedSplitDrag()
        {
            InventoryPanelView source = sourceView;
            if (source != null)
            {
                source.SetItemDragging(visualSourceItemId, false);
                source.ReleasePointer();
                source.HideDragGhost();
            }

            sourceView = null;
            draggedItem = null;
            dragSourceModel = null;
            visualSourceItemId = SerializableGuid.Empty;
            splitOriginalItemId = SerializableGuid.Empty;
            splitOriginalModel = null;
            isSplitDrag = false;
            ignoreNextRightRelease = false;
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;
            DragCoordinator.CancelDrag();
            ClearAllPreviews();
        }

        public bool IsDraggingFrom(InventoryPanelView view) =>
            IsDragging && ReferenceEquals(sourceView, view);

        public void UpdateDragTarget(Vector2 screenPosition)
        {
            if (!IsDragging) return;
            lastScreenPosition = screenPosition;
            InventoryPanelView target = FindPanelAt(screenPosition);
            if (target != null && !ReferenceEquals(target, hoveredPanelDuringDrag))
            {
                hoveredPanelDuringDrag = target;
                BringToFront(target);
            }
            else if (target == null)
            {
                hoveredPanelDuringDrag = null;
            }
            SerializableGuid targetId = target?.CurrentModel?.Id ?? SerializableGuid.Empty;
            DragCoordinator.UpdateTarget(targetId);
            sourceView?.ShowDragGhost(draggedItem, screenPosition, draggedItemRotationQuarterTurns);
            RefreshPlacementPreview(screenPosition);
        }

        /// <summary>
        /// Rotates the currently dragged item by one quarter-turn. Positive values rotate
        /// clockwise; negative values rotate counter-clockwise. The direction is normalized
        /// so callers can safely pass +1 or -1.
        /// </summary>
        public void RotateDraggedItem(int quarterTurnDirection)
        {
            if (!IsDragging || quarterTurnDirection == 0) return;
            draggedItemRotationQuarterTurns =
                ((draggedItemRotationQuarterTurns + quarterTurnDirection) % 4 + 4) % 4;
            sourceView?.ShowDragGhost(draggedItem, lastScreenPosition, draggedItemRotationQuarterTurns);
            RefreshPlacementPreview(lastScreenPosition);
        }

        /// <summary>Compatibility convenience method: rotate one quarter-turn clockwise.</summary>
        public void RotateDraggedItem() => RotateDraggedItem(1);

        public void EndDrag(Vector2 screenPosition, int pointerId)
        {
            if (!IsDragging || sourceView == null) return;

            InventoryPanelView source = sourceView;
            InventoryPanelView destination = FindPanelAt(screenPosition);
            SerializableGuid itemId = DragCoordinator.CurrentDrag.ItemInstanceId;
            Item itemForDrop = draggedItem;
            IInventoryModel activeSourceModel = dragSourceModel ?? source.CurrentModel;
            bool wasSplitDrag = isSplitDrag;
            SerializableGuid originalSplitId = splitOriginalItemId;
            IInventoryModel originalSplitModel = splitOriginalModel;
            SerializableGuid visualItemId = visualSourceItemId;
            int finalRotation = draggedItemRotationQuarterTurns;
            InventoryDragContext context = DragCoordinator.CompleteDrag();

            source.ReleasePointer();
            source.SetItemDragging(visualItemId, false);
            sourceView = null;
            draggedItem = null;
            dragSourceModel = null;
            visualSourceItemId = SerializableGuid.Empty;
            splitOriginalItemId = SerializableGuid.Empty;
            splitOriginalModel = null;
            isSplitDrag = false;
            ignoreNextRightRelease = false;
            ClearAllPreviews();
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;

            if (context == null || activeSourceModel == null ||
                !activeSourceModel.TryGetItem(context.ItemInstanceId, out _))
            {
                if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, context != null ? context.ItemInstanceId : itemId);
                source.HideDragGhost();
                return;
            }
            if (destination == null || destination.CurrentModel == null)
            {
                if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, context.ItemInstanceId);
                source.HideDragGhost();
                return;
            }

            InventoryTransferResult result;
            if (destination.CurrentModel is IEquipmentInventoryModel destinationEquipment)
            {
                string destinationEquipmentSlot = destination.GetEquipmentSlotIdAt(screenPosition);
                if (string.IsNullOrEmpty(destinationEquipmentSlot))
                {
                    if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
                    source.HideDragGhost();
                    return;
                }

                if (destinationEquipment.TryGetItemAt(destinationEquipmentSlot, out Item equipmentStackTarget) &&
                    equipmentStackTarget != null && equipmentStackTarget.Id != itemId)
                {
                    InventoryQuickMoveResult equipmentMerge = QuickMoveService.MergeInto(activeSourceModel,
                        destination.CurrentModel, itemId, equipmentStackTarget.Id);
                    if (equipmentMerge.Success)
                    {
                        if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
                        TransferCompleted?.Invoke(new InventoryTransferResult(true, false,
                            InventoryTransferFailure.None, InventoryOperationFailure.None,
                            activeSourceModel.Id, destination.CurrentModel.Id, itemId, equipmentMerge.Message));
                        return;
                    }
                }

                if (activeSourceModel.Id == destination.CurrentModel.Id && activeSourceModel is IEquipmentInventoryModel localEquipment)
                {
                    InventoryOperationResult moveResult = localEquipment.Move(itemId, destinationEquipmentSlot);
                    bool moved = moveResult.Success;
                    InventoryOperationFailure localFailure = moveResult.Failure;
                    result = moved
                        ? new InventoryTransferResult(true, false, InventoryTransferFailure.None, InventoryOperationFailure.None,
                            activeSourceModel.Id, destination.CurrentModel.Id, itemId,
                            "Item moved to equipment slot '" + destinationEquipmentSlot + "'.")
                        : new InventoryTransferResult(false, false, InventoryTransferFailure.DestinationRejected, localFailure,
                            activeSourceModel.Id, destination.CurrentModel.Id, itemId,
                            "The selected equipment slot cannot accept this item: " + localFailure + ".");
                }
                else
                {
                    result = TransferService.TransferToEquipmentSlot(activeSourceModel, destinationEquipment,
                        itemId, destinationEquipmentSlot);
                }
            }
            else
            {
                int destinationSlot = destination.GetSlotIndexAt(screenPosition);
                if (destinationSlot < 0)
                {
                    if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
                    source.HideDragGhost();
                    return;
                }
                Item stackTarget = null;
                if (destination.CurrentModel is IGridInventoryModel targetGrid)
                {
                    int pointerX = destination.GetSlotIndexAt(screenPosition) % targetGrid.Width;
                    int pointerY = destination.GetSlotIndexAt(screenPosition) / targetGrid.Width;
                    targetGrid.TryGetItemAtCell(pointerX, pointerY, out stackTarget);
                    destinationSlot = GetPivotAdjustedSlot(destinationSlot, targetGrid.Width, targetGrid.Height, finalRotation, itemForDrop);
                    if (destinationSlot < 0 && stackTarget == null)
                    {
                        if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
                        source.HideDragGhost();
                        return;
                    }
                }
                else if (destination.CurrentModel is ISlotInventoryModel targetSlots)
                {
                    targetSlots.TryGetItemAt(destinationSlot, out stackTarget);
                }

                if (stackTarget != null && stackTarget.Id != itemId)
                {
                    InventoryQuickMoveResult merge = QuickMoveService.MergeInto(activeSourceModel,
                        destination.CurrentModel, itemId, stackTarget.Id);
                    if (merge.Success)
                    {
                        if (wasSplitDrag) RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
                        TransferCompleted?.Invoke(new InventoryTransferResult(true, false,
                            InventoryTransferFailure.None, InventoryOperationFailure.None,
                            activeSourceModel.Id, destination.CurrentModel.Id, itemId, merge.Message));
                        return;
                    }
                }

                if (activeSourceModel.Id == destination.CurrentModel.Id)
                {
                    InventoryOperationFailure localFailure = InventoryOperationFailure.None;
                    bool moved = false;
                    if (activeSourceModel is IGridInventoryModel localGrid)
                    {
                        int x = destinationSlot % localGrid.Width;
                        int y = destinationSlot / localGrid.Width;
                        InventoryOperationResult moveResult = localGrid.MoveTo(itemId, x, y, finalRotation);
                        moved = moveResult.Success;
                        localFailure = moveResult.Failure;
                    }
                    else if (activeSourceModel is ISlotInventoryModel localSlots)
                    {
                        InventoryOperationResult moveResult = localSlots.Move(itemId, destinationSlot);
                        moved = moveResult.Success;
                        localFailure = moveResult.Failure;
                    }

                    result = moved
                        ? new InventoryTransferResult(true, false, InventoryTransferFailure.None,
                            InventoryOperationFailure.None, activeSourceModel.Id, destination.CurrentModel.Id,
                            itemId, "Item moved to the selected location.")
                        : new InventoryTransferResult(false, false, InventoryTransferFailure.DestinationRejected,
                            localFailure, activeSourceModel.Id, destination.CurrentModel.Id, itemId,
                            "The selected location is occupied or unavailable: " + localFailure + ".");
                }
                else
                {
                    result = TransferService.TransferAt(activeSourceModel,
                        destination.CurrentModel, itemId, destinationSlot, finalRotation);
                }
            }

            TransferCompleted?.Invoke(result);
            if (!result.Success && wasSplitDrag)
                RestoreSplitRemainder(originalSplitModel, originalSplitId, activeSourceModel, itemId);
            if (!result.Success)
            {
                if (result.Failure == InventoryTransferFailure.RollbackFailed)
                    Debug.LogError($"Inventory operation failed: {result.Message}", this);
                else
                    Debug.LogWarning($"Inventory operation failed: {result.Message}", this);
            }
        }

        /// <summary>Removes the currently held item and hands it to the world-drop listener.</summary>
        public bool DropDraggedItem()
        {
            if (!IsDragging || sourceView == null || draggedItem == null || ItemDropped == null)
                return false;

            InventoryPanelView source = sourceView;
            SerializableGuid itemId = DragCoordinator.CurrentDrag.ItemInstanceId;
            IInventoryModel activeSourceModel = dragSourceModel ?? source.CurrentModel;
            InventoryOperationFailure failure = InventoryOperationFailure.None;
            InventoryOperationResult removal = activeSourceModel != null
                ? activeSourceModel.Remove(itemId)
                : null;
            if (removal == null || !removal.Success || removal.RemovedItem == null)
            {
                failure = removal != null ? removal.Failure : InventoryOperationFailure.InvalidItem;
                Debug.LogWarning("Could not drop held item: " + failure + ".", this);
                return false;
            }
            Item removedItem = removal.RemovedItem;

            source.SetItemDragging(visualSourceItemId, false);
            source.ReleasePointer();
            sourceView = null;
            draggedItem = null;
            dragSourceModel = null;
            visualSourceItemId = SerializableGuid.Empty;
            splitOriginalItemId = SerializableGuid.Empty;
            splitOriginalModel = null;
            isSplitDrag = false;
            ignoreNextRightRelease = false;
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;
            DragCoordinator.CancelDrag();
            ClearAllPreviews();
            source.HideDragGhost();
            ItemDropped?.Invoke(removedItem, source);
            return true;
        }

        public void CancelDrag()
        {
            InventoryPanelView source = sourceView;
            IInventoryModel activeSourceModel = dragSourceModel;
            IInventoryModel originalModel = splitOriginalModel;
            SerializableGuid splitId = splitOriginalItemId;
            SerializableGuid draggedId = DragCoordinator.CurrentDrag.ItemInstanceId;
            bool restoreSplit = isSplitDrag;
            if (source != null)
            {
                source.SetItemDragging(visualSourceItemId, false);
                source.ReleasePointer();
            }
            sourceView = null;
            draggedItem = null;
            dragSourceModel = null;
            visualSourceItemId = SerializableGuid.Empty;
            splitOriginalItemId = SerializableGuid.Empty;
            splitOriginalModel = null;
            isSplitDrag = false;
            ignoreNextRightRelease = false;
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;
            DragCoordinator.CancelDrag();
            ClearAllPreviews();
            if (restoreSplit && source != null)
                RestoreSplitRemainder(originalModel, splitId, activeSourceModel, draggedId);
            source?.HideDragGhost();
        }

        private void RestoreSplitRemainder(IInventoryModel originalModel, SerializableGuid originalItemId,
            IInventoryModel temporaryModel, SerializableGuid temporaryItemId)
        {
            if (originalModel == null || temporaryModel == null ||
                !temporaryModel.TryGetItem(temporaryItemId, out Item remainder) || remainder == null) return;

            if (originalModel.TryGetItem(originalItemId, out Item original) && original != null &&
                originalModel.SetQuantity(originalItemId, original.quantity + remainder.quantity).Success)
            {
                temporaryModel.Remove(temporaryItemId);
                return;
            }

            InventoryOperationResult detachResult = temporaryModel.Remove(temporaryItemId);
            if (detachResult.Success && detachResult.RemovedItem != null)
            {
                InventoryOperationResult restoreResult = originalModel.Add(detachResult.RemovedItem);
                if (!restoreResult.Success)
                    Debug.LogError("Could not restore a split stack to its original inventory: " + restoreResult.Failure + ".", this);
            }
        }

        private void RefreshPlacementPreview(Vector2 screenPosition)
        {
            ClearAllPreviews();
            if (sourceView == null || dragSourceModel == null ||
                !dragSourceModel.TryGetItem(DragCoordinator.CurrentDrag.ItemInstanceId, out Item item) ||
                item == null)
                return;

            InventoryPanelView destination = FindPanelAt(screenPosition);
            if (destination == null || destination.CurrentModel == null) return;

            if (destination.CurrentModel is IEquipmentInventoryModel equipment)
            {
                string equipmentSlotId = destination.GetEquipmentSlotIdAt(screenPosition);
                if (string.IsNullOrEmpty(equipmentSlotId)) return;
                bool equipmentSlotValid;
                if (equipment.TryGetItemAt(equipmentSlotId, out Item equipmentStackTarget) && equipmentStackTarget != null &&
                    equipmentStackTarget.Id != item.Id && QuickMoveService.CanMerge(item, equipmentStackTarget))
                {
                    equipmentSlotValid = true;
                }
                else if (dragSourceModel.Id == destination.CurrentModel.Id && dragSourceModel is IEquipmentInventoryModel localEquipment)
                {
                    equipmentSlotValid = localEquipment.TryGetSlotOf(item.Id, out string currentSlot);
                    if (equipmentSlotValid && currentSlot != equipmentSlotId)
                    {
                        equipmentSlotValid = !localEquipment.TryGetItemAt(equipmentSlotId, out _) && item.details != null &&
                            item.details.category == ItemCategory.Equipment;
                        if (equipmentSlotValid)
                        {
                            EquipmentSlotDefinition definition = null;
                            foreach (EquipmentSlotDefinition candidate in localEquipment.SlotDefinitions)
                                if (candidate.SlotId == equipmentSlotId) { definition = candidate; break; }
                            equipmentSlotValid = definition != null && definition.Accepts(item.details.equipmentType);
                        }
                    }
                }
                else
                {
                    equipmentSlotValid = equipment.CanAcceptAt(item, equipmentSlotId, out _);
                }
                destination.ShowEquipmentSlotPreview(equipmentSlotId, equipmentSlotValid);
                return;
            }

            if (!(destination.CurrentModel is IGridInventoryModel grid)) return;

            int index = destination.GetSlotIndexAt(screenPosition);
            if (index < 0) return;

            ItemGridShape.GetRotatedSize(item.details, draggedItemRotationQuarterTurns, out int width, out int height);
            Vector2 pivot = item.details != null ? item.details.GetRotatedDragPivot(draggedItemRotationQuarterTurns) : new Vector2(0.5f, 0.5f);
            int x = index % grid.Width - Mathf.FloorToInt(pivot.x * width);
            int y = index / grid.Width - Mathf.FloorToInt(pivot.y * height);

            bool valid;
            // Occupied grid cells are normally invalid placements, but a compatible
            // partial stack is a valid merge target and should receive the green preview.
            int pointerX = index % grid.Width;
            int pointerY = index / grid.Width;
            if (grid.TryGetItemAtCell(pointerX, pointerY, out Item stackTarget) &&
                stackTarget != null && stackTarget.Id != item.Id && QuickMoveService.CanMerge(item, stackTarget))
            {
                valid = true;
            }
            else if (dragSourceModel.Id == destination.CurrentModel.Id)
            {
                valid = grid.CanMoveTo(item.Id, x, y, draggedItemRotationQuarterTurns, out _);
            }
            else
            {
                valid = grid.CanPlaceAt(item, x, y, draggedItemRotationQuarterTurns, out _);
            }

            destination.ShowGridPlacementPreview(x, y, item, draggedItemRotationQuarterTurns, valid);
        }

        private int GetPivotAdjustedSlot(int pointerSlot, int gridWidth, int gridHeight, int rotationQuarterTurns, Item item)
        {
            if (item == null || item.details == null) return pointerSlot;
            ItemGridShape.GetRotatedSize(item.details, rotationQuarterTurns, out int width, out int height);
            Vector2 pivot = item.details.GetRotatedDragPivot(rotationQuarterTurns);
            int x = pointerSlot % gridWidth - Mathf.FloorToInt(pivot.x * width);
            int y = pointerSlot / gridWidth - Mathf.FloorToInt(pivot.y * height);
            if (x < 0 || y < 0 || x + width > gridWidth || y + height > gridHeight)
                return -1;
            return y * gridWidth + x;
        }

        private void ClearAllPreviews()
        {
            foreach (InventoryPanelView panel in panels)
            {
                if (panel == null) continue;
                panel.HideGridPlacementPreview();
                panel.ClearEquipmentSlotPreviews();
            }
        }

        private InventoryPanelView FindPanelAt(Vector2 screenPosition)
        {
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                InventoryPanelView panel = panels[i];
                if (panel != null && panel.CurrentModel != null && panel.ContainsScreenPoint(screenPosition))
                    return panel;
            }
            return null;
        }
    }
}
