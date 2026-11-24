using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>UI Toolkit presentation for one inventory panel hosted by a shared canvas.</summary>
    public sealed class InventoryPanelView : MonoBehaviour, IInventoryPanelView
    {
        [SerializeField] private InventoryPanelCanvas canvas;
        [Header("Panel Layout")]
        [SerializeField, Tooltip("Panel position as percentages of the shared canvas: X is left-to-right, Y is top-to-bottom.")] private Vector2 panelPositionPercent = new Vector2(4f, 4f);
        [Header("Quick Move")]
        [SerializeField, Min(0), Tooltip("Higher-priority destination panels are checked first when shift-clicking an item.")] private int quickMovePriority = 1;

        private InventoryPanelSession session;
        private VisualElement root;
        private VisualElement panel;
        private VisualElement slotsContainer;
        private VisualElement placementPreview;
        private readonly List<VisualElement> slotElements = new List<VisualElement>();
        private readonly List<VisualElement> itemElements = new List<VisualElement>();
        private IInventoryModel currentModel;
        private int capturedPointerId = -1;
        private float gridCellSize = 64f;
        private bool attached;

        public IInventoryModel CurrentModel => currentModel;
        public int QuickMovePriority => quickMovePriority;

        public void Initialize()
        {
            if (attached) return;
            if (canvas == null)
                throw new InvalidOperationException($"{name}: InventoryPanelCanvas is not assigned.");
            canvas.Register(this);
            if (session != null) session.Register(this);
            else Debug.LogWarning($"{name}: No InventoryPanelSession assigned; drag-and-drop is unavailable.", this);
        }

        internal void AttachToCanvas(InventoryPanelCanvas owner, VisualElement sharedRoot)
        {
            if (attached) return;
            canvas = owner;
            session = owner.Session;
            root = sharedRoot;

            panel = new VisualElement { name = $"inventoryPanel_{GetInstanceID()}" };
            panel.AddToClassList("inventory");
            panel.style.position = Position.Absolute;
            panel.style.left = Length.Percent(Mathf.Clamp(panelPositionPercent.x, 0f, 100f));
            panel.style.top = Length.Percent(Mathf.Clamp(panelPositionPercent.y, 0f, 100f));
            panel.style.maxWidth = Length.Percent(48f);
            panel.style.maxHeight = Length.Percent(92f);
            root.Add(panel);

            slotsContainer = new VisualElement { name = "slotsContainer" };
            slotsContainer.AddToClassList("slotsContainer");
            panel.Add(slotsContainer);

            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            attached = true;
        }

        public void SetHeader(string displayName)
        {
            if (panel == null) return;
            VisualElement header = panel.Q("inventoryPanelHeader");
            if (header == null)
            {
                header = new VisualElement { name = "inventoryPanelHeader" };
                header.AddToClassList("inventoryHeader");
                panel.Insert(0, header);
                canvas?.ConfigureTemporaryPanelTestingControls(panel, header, () =>
                {
                    if (session != null) session.BringToFront(this);
                });
            }
            header.Clear();
            header.Add(new Label(displayName));
        }

        public void Render(IInventoryModel model)
        {
            if (!attached) Initialize();
            currentModel = model;
            slotsContainer.Clear();
            slotsContainer.RemoveFromClassList("gridSlotsContainer");
            slotsContainer.RemoveFromClassList("equipmentSlotsContainer");
            slotElements.Clear();
            itemElements.Clear();
            placementPreview = null;
            if (model == null) { SetHeader("Inventory"); return; }
            SetHeader(model.DisplayName);

            if (model is IGridInventoryModel grid)
            {
                RenderGrid(grid);
                return;
            }

            if (model is IEquipmentInventoryModel equipment)
            {
                RenderEquipment(equipment);
                return;
            }

            panel.style.width = StyleKeyword.Auto;
            panel.style.height = StyleKeyword.Auto;
            slotsContainer.style.width = StyleKeyword.Auto;
            slotsContainer.style.height = StyleKeyword.Auto;
            slotsContainer.style.overflow = Overflow.Visible;
            for (int i = 0; i < model.Capacity; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("slot");
                slot.userData = i;
                slotsContainer.Add(slot);
                slotElements.Add(slot);

                Item item = i < model.Items.Count ? model.Items[i] : null;
                if (item != null)
                {
                    VisualElement itemElement = CreateItemElement(item);
                    slot.Add(itemElement);
                    itemElements.Add(itemElement);
                }
            }
        }

        private void RenderEquipment(IEquipmentInventoryModel equipment)
        {
            panel.style.width = StyleKeyword.Auto;
            panel.style.height = StyleKeyword.Auto;
            slotsContainer.style.width = StyleKeyword.Auto;
            slotsContainer.style.height = StyleKeyword.Auto;
            slotsContainer.style.overflow = Overflow.Visible;
            slotsContainer.AddToClassList("equipmentSlotsContainer");

            foreach (EquipmentSlotDefinition definition in equipment.SlotDefinitions)
            {
                var slot = new VisualElement { name = "equipmentSlot_" + definition.SlotId, userData = definition.SlotId };
                slot.AddToClassList("slot");
                slot.AddToClassList("equipmentSlot");
                slot.tooltip = definition.DisplayName;
                slotsContainer.Add(slot);
                slotElements.Add(slot);

                if (equipment.TryGetItemAt(definition.SlotId, out Item equippedItem) && equippedItem != null)
                {
                    VisualElement itemElement = CreateItemElement(equippedItem);
                    itemElement.AddToClassList("equipmentSlotItem");
                    slot.Add(itemElement);
                    itemElements.Add(itemElement);
                }

                var label = new Label(definition.DisplayName);
                label.AddToClassList("equipmentSlotLabel");
                label.pickingMode = PickingMode.Ignore;
                slot.Add(label);
            }
        }

        private void RenderGrid(IGridInventoryModel grid)
        {
            gridCellSize = CalculateGridCellSize(grid);
            slotsContainer.AddToClassList("gridSlotsContainer");
            slotsContainer.style.width = grid.Width * gridCellSize;
            slotsContainer.style.height = grid.Height * gridCellSize;
            // Clip item previews to the actual grid bounds so invalid footprints never
            // paint outside the inventory when the pointer is near an outer edge.
            slotsContainer.style.overflow = Overflow.Hidden;
            panel.style.width = grid.Width * gridCellSize + 20f;
            panel.style.height = grid.Height * gridCellSize + 70f;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new VisualElement();
                cell.AddToClassList("slot");
                cell.AddToClassList("gridCell");
                cell.style.width = gridCellSize;
                cell.style.height = gridCellSize;
                cell.userData = y * grid.Width + x;
                cell.style.position = Position.Absolute;
                cell.style.left = x * gridCellSize;
                cell.style.top = y * gridCellSize;
                slotsContainer.Add(cell);
                slotElements.Add(cell);
            }

            foreach (Item item in grid.Items)
            {
                if (item == null || !grid.TryGetPlacement(item.Id, out GridItemPlacement placement)) continue;
                VisualElement itemElement = CreateItemElement(item);
                Image itemIcon = itemElement.Q<Image>(className: "slotIcon");
                if (itemIcon != null)
                {
                    int baseWidth = item.details != null ? Mathf.Max(1, item.details.GridWidth) : 1;
                    int baseHeight = item.details != null ? Mathf.Max(1, item.details.GridHeight) : 1;
                    float iconWidth = baseWidth * gridCellSize - 4f;
                    float iconHeight = baseHeight * gridCellSize - 4f;
                    itemIcon.style.position = Position.Absolute;
                    itemIcon.style.width = iconWidth;
                    itemIcon.style.height = iconHeight;
                    itemIcon.style.left = (placement.Width * gridCellSize - 4f - iconWidth) * 0.5f;
                    itemIcon.style.top = (placement.Height * gridCellSize - 4f - iconHeight) * 0.5f;
                    itemIcon.style.right = StyleKeyword.Auto;
                    itemIcon.style.bottom = StyleKeyword.Auto;
                    itemIcon.style.rotate = new Rotate(new Angle(placement.RotationDegrees, AngleUnit.Degree));
                }
                itemElement.RemoveFromClassList("slotItem");
                itemElement.AddToClassList("gridSlotItem");
                itemElement.style.position = Position.Absolute;
                itemElement.style.left = placement.X * gridCellSize + 2f;
                itemElement.style.top = placement.Y * gridCellSize + 2f;
                itemElement.style.width = placement.Width * gridCellSize - 4f;
                itemElement.style.height = placement.Height * gridCellSize - 4f;
                slotsContainer.Add(itemElement);
                itemElements.Add(itemElement);
            }

            placementPreview = new VisualElement();
            placementPreview.AddToClassList("gridPlacementPreview");
            placementPreview.pickingMode = PickingMode.Ignore;
            placementPreview.style.display = DisplayStyle.None;
            slotsContainer.Add(placementPreview);
        }

        private float CalculateGridCellSize(IGridInventoryModel grid)
        {
            float viewportWidth = root != null ? root.resolvedStyle.width : 1280f;
            float viewportHeight = root != null ? root.resolvedStyle.height : 720f;
            if (float.IsNaN(viewportWidth) || viewportWidth <= 0f) viewportWidth = 1280f;
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0f) viewportHeight = 720f;
            float availableWidth = Mathf.Max(24f, viewportWidth * 0.44f - 44f);
            float availableHeight = Mathf.Max(24f, viewportHeight * 0.78f - 76f);
            return Mathf.Clamp(Mathf.Min(64f, availableWidth / grid.Width, availableHeight / grid.Height), 8f, 64f);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            if (currentModel is IGridInventoryModel grid)
                Render(grid);
        }

        public void FocusForDrag() => canvas?.Focus();

        internal void BringPanelToFront()
        {
            canvas?.BringPanelToFront(panel);
        }

        public void ShowGridPlacementPreview(int x, int y, int width, int height, bool valid)
        {
            if (placementPreview == null) return;
            placementPreview.RemoveFromClassList("valid");
            placementPreview.RemoveFromClassList("invalid");
            placementPreview.AddToClassList(valid ? "valid" : "invalid");
            placementPreview.style.left = x * gridCellSize + 2f;
            placementPreview.style.top = y * gridCellSize + 2f;
            placementPreview.style.width = width * gridCellSize - 4f;
            placementPreview.style.height = height * gridCellSize - 4f;
            placementPreview.style.display = DisplayStyle.Flex;
        }

        public void HideGridPlacementPreview()
        {
            if (placementPreview != null) placementPreview.style.display = DisplayStyle.None;
        }

        internal void ShowEquipmentSlotPreview(string slotId, bool valid)
        {
            foreach (VisualElement slot in slotElements)
            {
                if (!Equals(slot.userData, slotId)) continue;
                slot.RemoveFromClassList("equipmentSlot-valid");
                slot.RemoveFromClassList("equipmentSlot-invalid");
                slot.AddToClassList(valid ? "equipmentSlot-valid" : "equipmentSlot-invalid");
                return;
            }
        }

        internal void ClearEquipmentSlotPreviews()
        {
            foreach (VisualElement slot in slotElements)
            {
                slot.RemoveFromClassList("equipmentSlot-valid");
                slot.RemoveFromClassList("equipmentSlot-invalid");
            }
        }

        internal void SetItemDragging(SerializableGuid itemId, bool dragging)
        {
            foreach (VisualElement element in itemElements)
            {
                if (!Equals(element.userData, itemId)) continue;
                if (dragging) element.AddToClassList("inventory-item-dragging");
                else element.RemoveFromClassList("inventory-item-dragging");
            }
        }

        internal void ShowDragGhost(Item item, Vector2 screenPosition, int rotationQuarterTurns)
        {
            if (item == null) return;
            Vector2 pivot = item.details != null ? item.details.DragPivot : new Vector2(0.5f, 0.5f);
            float width = gridCellSize;
            float height = gridCellSize;
            if (currentModel is IGridInventoryModel grid && item.details != null)
            {
                int itemWidth = Mathf.Max(1, item.details.GridWidth);
                int itemHeight = Mathf.Max(1, item.details.GridHeight);
                width *= itemWidth;
                height *= itemHeight;
            }
            canvas?.ShowDragGhost(screenPosition, item.details != null ? item.details.Icon : null,
                new Vector2(width, height), pivot, rotationQuarterTurns);
        }

        public void Clear()
        {
            currentModel = null;
            slotsContainer?.Clear();
            placementPreview = null;
            slotElements.Clear();
            itemElements.Clear();
            SetHeader("Inventory");
        }

        public bool ContainsScreenPoint(Vector2 screenPosition) =>
            panel != null && panel.worldBound.Contains(screenPosition);

        public int GetSlotIndexAt(Vector2 screenPosition)
        {
            for (int i = slotElements.Count - 1; i >= 0; i--)
                if (slotElements[i].worldBound.Contains(screenPosition)) return i;
            return -1;
        }

        public string GetEquipmentSlotIdAt(Vector2 screenPosition)
        {
            if (!(currentModel is IEquipmentInventoryModel)) return null;
            for (int i = slotElements.Count - 1; i >= 0; i--)
            {
                VisualElement slot = slotElements[i];
                if (slot.worldBound.Contains(screenPosition)) return slot.userData as string;
            }
            return null;
        }

        public void CapturePointer(int pointerId)
        {
            capturedPointerId = pointerId;
            root?.CapturePointer(pointerId);
        }

        public void ReleasePointer()
        {
            if (capturedPointerId >= 0 && root != null && root.HasPointerCapture(capturedPointerId))
                root.ReleasePointer(capturedPointerId);
            capturedPointerId = -1;
        }

        private VisualElement CreateItemElement(Item item)
        {
            var element = new VisualElement();
            element.AddToClassList("slotItem");
            element.userData = item.Id;
            element.tooltip = "Drag to move. Shift-click to quick move.";
            var icon = new Image();
            icon.AddToClassList("slotIcon");
            if (item.details != null && item.details.Icon != null)
            {
                icon.sprite = item.details.Icon;
                icon.scaleMode = ScaleMode.ScaleToFit;
            }
            else icon.AddToClassList("slotIconMissing");
            element.Add(icon);
            if (item.quantity > 1) element.Add(new Label(item.quantity.ToString()) { name = "stackCount" });

            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || session == null || currentModel == null) return;
                if ((evt.modifiers & EventModifiers.Shift) != 0)
                {
                    session.QuickMove(this, item.Id);
                    evt.StopPropagation();
                    return;
                }
                if (!session.BeginDrag(this, item.Id, evt.pointerId)) return;
                session.UpdateDragTarget(evt.position);
                evt.StopPropagation();
            });
            return element;
        }

        private void OnDestroy()
        {
            if (session != null) session.Unregister(this);
            if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            ReleasePointer();
        }
    }
}
