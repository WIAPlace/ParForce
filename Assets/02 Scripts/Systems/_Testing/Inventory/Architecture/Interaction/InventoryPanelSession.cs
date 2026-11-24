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

        public bool IsDragging => DragCoordinator.IsDragging;
        private InventoryPanelView sourceView;
        private int draggedItemRotationQuarterTurns;
        private Vector2 lastScreenPosition;
        private Item draggedItem;
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
            source.SetItemDragging(itemInstanceId, true);
            BringToFront(source);
            draggedItemRotationQuarterTurns = source.CurrentModel is IGridInventoryModel sourceGrid &&
                                 sourceGrid.TryGetPlacement(itemInstanceId, out GridItemPlacement placement)
                ? placement.RotationQuarterTurns : 0;
            source.FocusForDrag();
            source.CapturePointer(pointerId);
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

        public void RotateDraggedItem()
        {
            if (!IsDragging) return;
            draggedItemRotationQuarterTurns = (draggedItemRotationQuarterTurns + 1) % 4;
            sourceView?.ShowDragGhost(draggedItem, lastScreenPosition, draggedItemRotationQuarterTurns);
            RefreshPlacementPreview(lastScreenPosition);
        }

        public void EndDrag(Vector2 screenPosition, int pointerId)
        {
            if (!IsDragging || sourceView == null) return;

            InventoryPanelView source = sourceView;
            InventoryPanelView destination = FindPanelAt(screenPosition);
            SerializableGuid itemId = DragCoordinator.CurrentDrag.ItemInstanceId;
            Item itemForDrop = draggedItem;
            int finalRotation = draggedItemRotationQuarterTurns;
            InventoryDragContext context = DragCoordinator.CompleteDrag();

            source.ReleasePointer();
            source.SetItemDragging(itemId, false);
            sourceView = null;
            draggedItem = null;
            ClearAllPreviews();
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;

            if (context == null || destination == null || destination.CurrentModel == null ||
                !source.CurrentModel.TryGetItem(context.ItemInstanceId, out _))
                return;

            InventoryTransferResult result;
            if (destination.CurrentModel is IEquipmentInventoryModel destinationEquipment)
            {
                string destinationEquipmentSlot = destination.GetEquipmentSlotIdAt(screenPosition);
                if (string.IsNullOrEmpty(destinationEquipmentSlot)) return;

                if (source.CurrentModel.Id == destination.CurrentModel.Id && source.CurrentModel is IEquipmentInventoryModel localEquipment)
                {
                    bool moved = localEquipment.TryMove(itemId, destinationEquipmentSlot, out InventoryOperationFailure localFailure);
                    result = moved
                        ? new InventoryTransferResult(true, false, InventoryTransferFailure.None, InventoryOperationFailure.None,
                            source.CurrentModel.Id, destination.CurrentModel.Id, itemId,
                            "Item moved to equipment slot '" + destinationEquipmentSlot + "'.")
                        : new InventoryTransferResult(false, false, InventoryTransferFailure.DestinationRejected, localFailure,
                            source.CurrentModel.Id, destination.CurrentModel.Id, itemId,
                            "The selected equipment slot cannot accept this item: " + localFailure + ".");
                }
                else
                {
                    result = TransferService.TryTransferToEquipmentSlot(source.CurrentModel, destinationEquipment,
                        itemId, destinationEquipmentSlot);
                }
            }
            else
            {
                int destinationSlot = destination.GetSlotIndexAt(screenPosition);
                if (destinationSlot < 0) return;
                Item stackTarget = null;
                if (destination.CurrentModel is IGridInventoryModel targetGrid)
                {
                    int pointerX = destination.GetSlotIndexAt(screenPosition) % targetGrid.Width;
                    int pointerY = destination.GetSlotIndexAt(screenPosition) / targetGrid.Width;
                    targetGrid.TryGetItemAtCell(pointerX, pointerY, out stackTarget);
                    destinationSlot = GetPivotAdjustedSlot(destinationSlot, targetGrid.Width, targetGrid.Height, finalRotation, itemForDrop);
                    if (destinationSlot < 0 && stackTarget == null) return;
                }
                else if (destination.CurrentModel is ISlotInventoryModel targetSlots)
                {
                    targetSlots.TryGetItemAt(destinationSlot, out stackTarget);
                }

                if (stackTarget != null && stackTarget.Id != itemId)
                {
                    InventoryQuickMoveResult merge = QuickMoveService.TryMergeInto(source.CurrentModel,
                        destination.CurrentModel, itemId, stackTarget.Id);
                    if (merge.Success)
                    {
                        TransferCompleted?.Invoke(new InventoryTransferResult(true, false,
                            InventoryTransferFailure.None, InventoryOperationFailure.None,
                            source.CurrentModel.Id, destination.CurrentModel.Id, itemId, merge.Message));
                        return;
                    }
                }

                if (source.CurrentModel.Id == destination.CurrentModel.Id)
                {
                    InventoryOperationFailure localFailure = InventoryOperationFailure.None;
                    bool moved = false;
                    if (source.CurrentModel is IGridInventoryModel localGrid)
                    {
                        int x = destinationSlot % localGrid.Width;
                        int y = destinationSlot / localGrid.Width;
                        moved = localGrid.TryMoveTo(itemId, x, y, finalRotation, out localFailure);
                    }
                    else if (source.CurrentModel is ISlotInventoryModel localSlots)
                    {
                        moved = localSlots.TryMove(itemId, destinationSlot, out localFailure);
                    }

                    result = moved
                        ? new InventoryTransferResult(true, false, InventoryTransferFailure.None,
                            InventoryOperationFailure.None, source.CurrentModel.Id, destination.CurrentModel.Id,
                            itemId, "Item moved to the selected location.")
                        : new InventoryTransferResult(false, false, InventoryTransferFailure.DestinationRejected,
                            localFailure, source.CurrentModel.Id, destination.CurrentModel.Id, itemId,
                            "The selected location is occupied or unavailable: " + localFailure + ".");
                }
                else
                {
                    result = TransferService.TryTransferAt(source.CurrentModel,
                        destination.CurrentModel, itemId, destinationSlot, finalRotation);
                }
            }

            TransferCompleted?.Invoke(result);
            if (!result.Success)
            {
                if (result.Failure == InventoryTransferFailure.RollbackFailed)
                    Debug.LogError($"Inventory operation failed: {result.Message}", this);
                else
                    Debug.LogWarning($"Inventory operation failed: {result.Message}", this);
            }
        }

        public void CancelDrag()
        {
            if (sourceView != null)
            {
                sourceView.SetItemDragging(DragCoordinator.CurrentDrag.ItemInstanceId, false);
                sourceView.ReleasePointer();
            }
            sourceView = null;
            draggedItem = null;
            draggedItemRotationQuarterTurns = 0;
            hoveredPanelDuringDrag = null;
            DragCoordinator.CancelDrag();
            ClearAllPreviews();
        }

        private void RefreshPlacementPreview(Vector2 screenPosition)
        {
            ClearAllPreviews();
            if (sourceView == null || sourceView.CurrentModel == null ||
                !sourceView.CurrentModel.TryGetItem(DragCoordinator.CurrentDrag.ItemInstanceId, out Item item) ||
                item == null)
                return;

            InventoryPanelView destination = FindPanelAt(screenPosition);
            if (destination == null || destination.CurrentModel == null) return;

            if (destination.CurrentModel is IEquipmentInventoryModel equipment)
            {
                string equipmentSlotId = destination.GetEquipmentSlotIdAt(screenPosition);
                if (string.IsNullOrEmpty(equipmentSlotId)) return;
                bool equipmentSlotValid;
                if (sourceView.CurrentModel.Id == destination.CurrentModel.Id && sourceView.CurrentModel is IEquipmentInventoryModel localEquipment)
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

            int width = item.details != null ? Mathf.Max(1, item.details.GridWidth) : 1;
            int height = item.details != null ? Mathf.Max(1, item.details.GridHeight) : 1;
            if (draggedItemRotationQuarterTurns % 2 != 0)
            {
                int swap = width;
                width = height;
                height = swap;
            }
            Vector2 pivot = item.details != null ? item.details.DragPivot : new Vector2(0.5f, 0.5f);
            int x = index % grid.Width - Mathf.FloorToInt(pivot.x * width);
            int y = index / grid.Width - Mathf.FloorToInt(pivot.y * height);

            bool valid;
            if (sourceView.CurrentModel.Id == destination.CurrentModel.Id)
                valid = grid.CanMoveTo(item.Id, x, y, draggedItemRotationQuarterTurns, out _);
            else
                valid = grid.CanPlaceAt(item, x, y, draggedItemRotationQuarterTurns, out _);

            destination.ShowGridPlacementPreview(x, y, width, height, valid);
        }

        private int GetPivotAdjustedSlot(int pointerSlot, int gridWidth, int gridHeight, int rotationQuarterTurns, Item item)
        {
            if (item == null || item.details == null) return pointerSlot;
            int width = Mathf.Max(1, item.details.GridWidth);
            int height = Mathf.Max(1, item.details.GridHeight);
            if (rotationQuarterTurns % 2 != 0) { int swap = width; width = height; height = swap; }
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
