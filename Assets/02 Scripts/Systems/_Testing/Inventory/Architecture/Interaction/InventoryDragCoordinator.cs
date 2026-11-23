using System;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Coordinates a single drag operation across all panels in an inventory
    /// interaction session. It is an ordinary instance, never static state.
    /// A session/window should own one coordinator shared by its panels.
    /// </summary>
    public sealed class InventoryDragCoordinator
    {
        public InventoryDragContext CurrentDrag { get; private set; }
        public bool IsDragging => CurrentDrag != null;

        public event Action<InventoryDragContext> DragChanged;

        public bool TryBeginDrag(
            SerializableGuid sourceInventoryId,
            SerializableGuid itemInstanceId)
        {
            if (IsDragging ||
                sourceInventoryId == SerializableGuid.Empty ||
                itemInstanceId == SerializableGuid.Empty)
            {
                return false;
            }

            CurrentDrag = new InventoryDragContext(
                sourceInventoryId,
                itemInstanceId);

            DragChanged?.Invoke(CurrentDrag);
            return true;
        }

        public void UpdateTarget(SerializableGuid targetInventoryId)
        {
            if (!IsDragging)
                return;

            CurrentDrag.SetTarget(targetInventoryId);
            DragChanged?.Invoke(CurrentDrag);
        }

        public InventoryDragContext CompleteDrag()
        {
            InventoryDragContext completed = CurrentDrag;
            CurrentDrag = null;

            if (completed != null)
                DragChanged?.Invoke(null);

            return completed;
        }

        public void CancelDrag()
        {
            if (!IsDragging)
                return;

            CurrentDrag = null;
            DragChanged?.Invoke(null);
        }
    }

    public sealed class InventoryDragContext
    {
        public SerializableGuid SourceInventoryId { get; }
        public SerializableGuid ItemInstanceId { get; }
        public SerializableGuid TargetInventoryId { get; private set; }

        internal InventoryDragContext(
            SerializableGuid sourceInventoryId,
            SerializableGuid itemInstanceId)
        {
            SourceInventoryId = sourceInventoryId;
            ItemInstanceId = itemInstanceId;
            TargetInventoryId = SerializableGuid.Empty;
        }

        internal void SetTarget(SerializableGuid targetInventoryId)
        {
            TargetInventoryId = targetInventoryId;
        }
    }
}
