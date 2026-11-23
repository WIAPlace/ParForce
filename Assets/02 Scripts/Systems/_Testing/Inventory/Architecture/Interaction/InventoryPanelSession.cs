using System;
using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Shared interaction context for all inventory panels opened together.
    /// Put one instance on the inventory-window/session root and reference it
    /// from every InventoryPanelHost in that window.
    /// </summary>
    public sealed class InventoryPanelSession : MonoBehaviour
    {
        private readonly List<InventoryPanelView> panels = new List<InventoryPanelView>();

        public InventoryDragCoordinator DragCoordinator { get; } =
            new InventoryDragCoordinator();

        public InventoryTransferService TransferService { get; } =
            new InventoryTransferService();

        /// <summary>Raised after a drop has been validated and attempted.</summary>
        public event Action<InventoryTransferResult> TransferCompleted;

        public bool IsDragging => DragCoordinator.IsDragging;
        private InventoryPanelView sourceView;

        public void Register(InventoryPanelView view)
        {
            if (view != null && !panels.Contains(view))
                panels.Add(view);
        }

        public void Unregister(InventoryPanelView view)
        {
            if (view == null)
                return;

            panels.Remove(view);

            if (ReferenceEquals(sourceView, view))
                CancelDrag();
        }

        public bool BeginDrag(
            InventoryPanelView source,
            SerializableGuid itemInstanceId,
            int pointerId)
        {
            if (source == null || source.CurrentModel == null || IsDragging)
                return false;

            if (!source.CurrentModel.TryGetItem(itemInstanceId, out _))
                return false;

            if (!DragCoordinator.TryBeginDrag(
                    source.CurrentModel.Id,
                    itemInstanceId))
                return false;

            sourceView = source;
            source.CapturePointer(pointerId);
            return true;
        }

        public bool IsDraggingFrom(InventoryPanelView view)
        {
            return IsDragging && ReferenceEquals(sourceView, view);
        }

        public void UpdateDragTarget(Vector2 screenPosition)
        {
            InventoryPanelView target = FindPanelAt(screenPosition);
            SerializableGuid targetId = target?.CurrentModel?.Id ?? SerializableGuid.Empty;
            DragCoordinator.UpdateTarget(targetId);
        }

        public void EndDrag(Vector2 screenPosition, int pointerId)
        {
            if (!IsDragging || sourceView == null)
                return;

            InventoryPanelView source = sourceView;
            InventoryPanelView destination = FindPanelAt(screenPosition);
            InventoryDragContext context = DragCoordinator.CompleteDrag();

            source.ReleasePointer();
            sourceView = null;

            if (context == null || destination == null || destination.CurrentModel == null ||
                !source.CurrentModel.TryGetItem(context.ItemInstanceId, out _))
                return;

            int destinationSlot = destination.GetSlotIndexAt(screenPosition);
            if (destinationSlot < 0)
                return;

            InventoryTransferResult result;
            if (source.CurrentModel.Id == destination.CurrentModel.Id)
            {
                InventoryOperationFailure localFailure = InventoryOperationFailure.None;
                bool moved = source.CurrentModel is ISlotInventoryModel localSlots &&
                    localSlots.TryMove(context.ItemInstanceId, destinationSlot, out localFailure);
                if (moved)
                {
                    result = new InventoryTransferResult(true, false, InventoryTransferFailure.None,
                        InventoryOperationFailure.None, source.CurrentModel.Id, destination.CurrentModel.Id,
                        context.ItemInstanceId, "Item moved to the selected slot.");
                }
                else
                {
                    result = new InventoryTransferResult(false, false,
                        InventoryTransferFailure.DestinationRejected, localFailure,
                        source.CurrentModel.Id, destination.CurrentModel.Id, context.ItemInstanceId,
                        "The selected slot is occupied or unavailable: " + localFailure + ".");
                }
            }
            else
            {
                result = TransferService.TryTransferAt(source.CurrentModel,
                    destination.CurrentModel, context.ItemInstanceId, destinationSlot);
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
                sourceView.ReleasePointer();

            sourceView = null;
            DragCoordinator.CancelDrag();
        }

        private InventoryPanelView FindPanelAt(Vector2 screenPosition)
        {
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                InventoryPanelView panel = panels[i];
                if (panel != null && panel.CurrentModel != null &&
                    panel.ContainsScreenPoint(screenPosition))
                    return panel;
            }

            return null;
        }
    }
}
