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
        // Model changes rebuild the visual tree. Keep drag state by item ID so a
        // held source remains faded when quantity changes trigger a re-render.
        private readonly HashSet<SerializableGuid> draggingItemIds = new HashSet<SerializableGuid>();
        private IInventoryModel currentModel;
        private InventoryAppearance currentAppearance;
        private StyleSheet appliedAppearanceStyleSheet;
        private int capturedPointerId = -1;
        private float gridCellSize = 64f;
        private bool attached;

        public IInventoryModel CurrentModel => currentModel;
        public int QuickMovePriority => quickMovePriority;

        internal void ApplyGlobalScale(float scale)
        {
            if (panel == null) return;
            panel.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f), 0f);
            panel.style.scale = new Scale(new Vector2(scale, scale));
        }

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

        public void SetAppearance(InventoryAppearance appearance)
        {
            if (panel == null)
            {
                currentAppearance = appearance;
                return;
            }

            if (appliedAppearanceStyleSheet != null)
                panel.styleSheets.Remove(appliedAppearanceStyleSheet);

            currentAppearance = appearance;
            appliedAppearanceStyleSheet = currentAppearance != null ? currentAppearance.StyleSheet : null;
            if (appliedAppearanceStyleSheet != null)
                panel.styleSheets.Add(appliedAppearanceStyleSheet);

            SetSpriteBackground(panel, currentAppearance != null ? currentAppearance.PanelBackground : null);
            VisualElement header = panel.Q("inventoryPanelHeader");
            if (header != null)
                SetSpriteBackground(header, currentAppearance != null ? currentAppearance.HeaderBackground : null);

            if (currentModel != null)
                Render(currentModel);
        }

        public void SetPanelVisible(bool visible)
        {
            if (panel == null) return;
            if (!visible)
                CancelActiveDrag();
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void CancelActiveDrag()
        {
            session?.CancelDragFrom(this);
        }

        private void SetSpriteBackground(VisualElement element, Sprite sprite)
        {
            if (element == null) return;
            if (sprite != null)
                element.style.backgroundImage = new StyleBackground(sprite);
            else
                element.style.backgroundImage = StyleKeyword.Null;
        }

        private void ApplySlotAppearance(VisualElement slot, bool blocked = false)
        {
            Sprite sprite = currentAppearance == null
                ? null
                : blocked ? currentAppearance.BlockedCellBackground : currentAppearance.SlotBackground;
            SetSpriteBackground(slot, sprite);
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
            SetSpriteBackground(header, currentAppearance != null ? currentAppearance.HeaderBackground : null);
        }

        public void Render(IInventoryModel model)
        {
            if (!attached) Initialize();
            currentModel = model;
            panel.style.display = DisplayStyle.Flex;
            SetSpriteBackground(panel, currentAppearance != null ? currentAppearance.PanelBackground : null);
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
                ApplySlotAppearance(slot);
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
                ApplySlotAppearance(slot);
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
                if (!grid.IsCellUsable(x, y))
                {
                    cell.AddToClassList("gridCellBlocked");
                    ApplySlotAppearance(cell, true);
                    cell.tooltip = "Unavailable inventory space";
                    cell.pickingMode = PickingMode.Ignore;
                }
                else
                {
                    ApplySlotAppearance(cell);
                    cell.tooltip = "Usable inventory space";
                }
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
                VisualElement itemElement = CreateItemElement(item, false);
                itemElement.pickingMode = PickingMode.Ignore;
                ItemGridShape.GetRotatedSize(item.details, placement.RotationQuarterTurns, out int baseWidth, out int baseHeight);
                for (int shapeY = 0; shapeY < placement.Height; shapeY++)
                for (int shapeX = 0; shapeX < placement.Width; shapeX++)
                {
                    if (!ItemGridShape.IsOccupied(item.details, shapeX, shapeY, placement.RotationQuarterTurns)) continue;
                    var occupiedCell = new VisualElement();
                    occupiedCell.AddToClassList("gridItemOccupiedCell");
                    SetSpriteBackground(occupiedCell,
                        currentAppearance != null ? currentAppearance.OccupiedItemCellBackground : null);
                    occupiedCell.pickingMode = PickingMode.Position;
                    occupiedCell.tooltip = "Drag to move. Shift-click to quick move. Right-click a stack to split it.";
                    RegisterItemPointerHandler(occupiedCell, item);
                    occupiedCell.style.position = Position.Absolute;
                    occupiedCell.style.left = shapeX * gridCellSize;
                    occupiedCell.style.top = shapeY * gridCellSize;
                    occupiedCell.style.width = gridCellSize - 4f;
                    occupiedCell.style.height = gridCellSize - 4f;
                    itemElement.Insert(0, occupiedCell);
                }
                Image itemIcon = itemElement.Q<Image>(className: "slotIcon");
                if (itemIcon != null)
                {
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

        public void ShowGridPlacementPreview(int x, int y, Item item, int rotationQuarterTurns, bool valid)
        {
            if (placementPreview == null) return;
            placementPreview.Clear();
            placementPreview.RemoveFromClassList("valid");
            placementPreview.RemoveFromClassList("invalid");
            placementPreview.AddToClassList(valid ? "valid" : "invalid");
            ItemGridShape.GetRotatedSize(item != null ? item.details : null, rotationQuarterTurns,
                out int width, out int height);
            placementPreview.style.left = x * gridCellSize + 2f;
            placementPreview.style.top = y * gridCellSize + 2f;
            placementPreview.style.width = width * gridCellSize - 4f;
            placementPreview.style.height = height * gridCellSize - 4f;
            for (int cellY = 0; cellY < height; cellY++)
            for (int cellX = 0; cellX < width; cellX++)
            {
                if (!ItemGridShape.IsOccupied(item != null ? item.details : null, cellX, cellY, rotationQuarterTurns)) continue;
                var cell = new VisualElement();
                cell.AddToClassList("gridPlacementPreviewCell");
                cell.pickingMode = PickingMode.Ignore;
                cell.style.position = Position.Absolute;
                cell.style.left = cellX * gridCellSize;
                cell.style.top = cellY * gridCellSize;
                cell.style.width = gridCellSize - 4f;
                cell.style.height = gridCellSize - 4f;
                placementPreview.Add(cell);
            }
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
            if (dragging) draggingItemIds.Add(itemId);
            else draggingItemIds.Remove(itemId);

            // Update the current visual immediately. The ID set above also ensures
            // the state survives a model Changed event that rebuilds these elements.
            foreach (VisualElement element in itemElements)
            {
                if (!Equals(element.userData, itemId)) continue;
                if (dragging) element.AddToClassList("inventory-item-dragging");
                else element.RemoveFromClassList("inventory-item-dragging");
            }
        }

        internal void HideDragGhost()
        {
            canvas?.HideDragGhost();
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

        private VisualElement CreateItemElement(Item item, bool registerPointerHandler = true)
        {
            var element = new VisualElement();
            element.AddToClassList("slotItem");
            element.userData = item.Id;
            if (draggingItemIds.Contains(item.Id))
                element.AddToClassList("inventory-item-dragging");
            element.tooltip = "Drag to move. Shift-click to quick move. Right-click a stack to split it.";
            var icon = new Image();
            icon.AddToClassList("slotIcon");
            if (item.details != null && item.details.Icon != null)
            {
                icon.sprite = item.details.Icon;
                icon.scaleMode = ScaleMode.ScaleToFit;
            }
            else icon.AddToClassList("slotIconMissing");
            icon.pickingMode = PickingMode.Ignore;
            element.Add(icon);
            if (item.quantity > 1)
            {
                var countLabel = new Label(item.quantity.ToString()) { name = "stackCount" };
                countLabel.pickingMode = PickingMode.Ignore;
                element.Add(countLabel);
            }

            if (registerPointerHandler)
                RegisterItemPointerHandler(element, item);
            return element;
        }

        // ITEM PRESS / CLICK ENTRY POINT:
        // Add item-specific pointer actions here (for example, right-click inspect or
        // modifier-click actions). Keep the actual inventory operation in the session/service
        // layer rather than putting model mutation directly in this view callback.
        private void RegisterItemPointerHandler(VisualElement target, Item item)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (session == null || currentModel == null) return;
                if (evt.button == 1)
                {
                    if (!session.BeginSplitDrag(this, item.Id, evt.pointerId)) return;
                    session.UpdateDragTarget(evt.position);
                    evt.StopPropagation();
                    return;
                }
                if (evt.button != 0) return;
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
        }

        private void OnDisable()
        {
            // A panel can be hidden by its host or disabled directly; either way an
            // active drag must be cancelled and its shared-canvas ghost removed.
            session?.CancelDragFrom(this);
        }

        private void OnDestroy()
        {
            if (session != null) session.Unregister(this);
            if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            ReleasePointer();
        }
    }
}
