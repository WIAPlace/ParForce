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
        public event Action<InventoryTransferResult> TransferCompleted;
        public event Action<InventoryQuickMoveResult> QuickMoveCompleted;

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
            if (!source.CurrentModel.TrySetQuantity(original.Id, remainingQuantity, out _))
                return false;

            Item splitItem = new Item(original.details, heldQuantity);
            CollectionInventoryModel temporarySource = new CollectionInventoryModel(
                SerializableGuid.NewGuid(), "Held Stack", 1, new[] { splitItem });
            if (!DragCoordinator.TryBeginDrag(temporarySource.Id, splitItem.Id))
            {
                source.CurrentModel.TrySetQuantity(original.Id, originalQuantity, out _);
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
            if (!IsDragging || dragSourceModel == null || draggedItem == null || draggedItem.quantity <= 1 ||
                draggedItem.details == null)
                return false;

            InventoryPanelView destination = FindPanelAt(screenPosition);
            if (destination == null || destination.CurrentModel == null ||
                destination.CurrentModel is IEquipmentInventoryModel) return false;

            int slot = destination.GetSlotIndexAt(screenPosition);
            if (slot < 0) return false;

            Item target = null;
            IGridInventoryModel grid = destination.CurrentModel as IGridInventoryModel;
            if (grid != null)
            {
                int x = slot % grid.Width;
                int y = slot / grid.Width;
                grid.TryGetItemAtCell(x, y, out target);
            }
            else if (destination.CurrentModel is ISlotInventoryModel slots)
            {
                slots.TryGetItemAt(slot, out target);
            }
            else return false;

            if (target != null)
            {
                if (target.Id == draggedItem.Id || !QuickMoveService.CanMerge(draggedItem, target)) return false;
                int oldSourceQuantity = draggedItem.quantity;
                if (!dragSourceModel.TrySetQuantity(draggedItem.Id, oldSourceQuantity - 1, out _)) return false;
                if (!destination.CurrentModel.TrySetQuantity(target.Id, target.quantity + 1, out _))
                {
                    dragSourceModel.TrySetQuantity(draggedItem.Id, oldSourceQuantity, out _);
                    return false;
                }
                draggedItem.quantity = oldSourceQuantity - 1;
                UpdateDragTarget(screenPosition);
                return true;
            }

            Item oneItem = new Item(draggedItem.details, 1);
            InventoryOperationFailure failure;
            bool canAdd;
            if (grid != null)
            {
                int x = slot % grid.Width;
                int y = slot / grid.Width;
                canAdd = grid.CanPlaceAt(oneItem, x, y, draggedItemRotationQuarterTurns, out failure);
            }
            else
            {
                canAdd = ((ISlotInventoryModel)destination.CurrentModel).CanAcceptAt(oneItem, slot, out failure);
            }
            if (!canAdd) return false;

            int previousQuantity = draggedItem.quantity;
            if (!dragSourceModel.TrySetQuantity(draggedItem.Id, previousQuantity - 1, out _)) return false;
            bool added = grid != null
                ? grid.TryAddAt(oneItem, slot % grid.Width, slot / grid.Width, draggedItemRotationQuarterTurns, out failure)
                : ((ISlotInventoryModel)destination.CurrentModel).TryAddAt(oneItem, slot, out failure);
            if (!added)
            {
                dragSourceModel.TrySetQuantity(draggedItem.Id, previousQuantity, out _);
                return false;
            }
            draggedItem.quantity = previousQuantity - 1;
            UpdateDragTarget(screenPosition);
            return true;
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
                    InventoryQuickMoveResult equipmentMerge = QuickMoveService.TryMergeInto(activeSourceModel,
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
                    bool moved = localEquipment.TryMove(itemId, destinationEquipmentSlot, out InventoryOperationFailure localFailure);
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
                    result = TransferService.TryTransferToEquipmentSlot(activeSourceModel, destinationEquipment,
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
                    InventoryQuickMoveResult merge = QuickMoveService.TryMergeInto(activeSourceModel,
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
                        moved = localGrid.TryMoveTo(itemId, x, y, finalRotation, out localFailure);
                    }
                    else if (activeSourceModel is ISlotInventoryModel localSlots)
                    {
                        moved = localSlots.TryMove(itemId, destinationSlot, out localFailure);
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
                    result = TransferService.TryTransferAt(activeSourceModel,
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
            if (activeSourceModel == null || !activeSourceModel.TryRemove(itemId, out Item removedItem, out failure))
            {
                Debug.LogWarning("Could not drop held item: " + failure + ".", this);
                return false;
            }

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
                originalModel.TrySetQuantity(originalItemId, original.quantity + remainder.quantity, out _))
            {
                temporaryModel.TryRemove(temporaryItemId, out _, out _);
                return;
            }

            if (temporaryModel.TryRemove(temporaryItemId, out Item detached, out _))
            {
                if (!originalModel.TryAdd(detached, out InventoryOperationFailure restoreFailure))
                    Debug.LogError("Could not restore a split stack to its original inventory: " + restoreFailure + ".", this);
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
            Vector2 pivot = item.details != null ? item.details.DragPivot : new Vector2(0.5f, 0.5f);
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
            Vector2 pivot = item.details.DragPivot;
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
