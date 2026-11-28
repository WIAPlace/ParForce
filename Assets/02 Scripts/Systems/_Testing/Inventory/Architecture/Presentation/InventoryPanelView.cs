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
        [Header("Item Tooltips")]
        [SerializeField, Min(0f), Tooltip("How long the pointer must remain over an item before its tooltip appears.")] private float tooltipDelay = 0.55f;
        [SerializeField, Min(0f)] private float tooltipOffset = 18f;
        [SerializeField, Min(120f)] private float tooltipMaxWidth = 280f;
        [SerializeField] private StyleSheet tooltipStyleSheet;
        [Header("Item Hover Outline")]
        [SerializeField] private Color hoverOutlineColor = new Color(0.92f, 0.82f, 0.42f, 1f);
        [SerializeField, Min(1f)] private float hoverOutlineWidth = 2f;

        private InventoryPanelSession session;
        private VisualElement root;
        private VisualElement panel;
        private VisualElement slotsContainer;
        private VisualElement placementPreview;
        private VisualElement activeContextMenu;
        private VisualElement activeTooltip;
        private IVisualElementScheduledItem tooltipShowSchedule;
        private Item hoveredTooltipItem;
        private int hoveredTooltipRotation;
        private Vector2 lastTooltipPointerPosition;
        private readonly List<VisualElement> hoveredOutlineCells = new List<VisualElement>();
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
        private bool interactionEnabled = true;

        public IInventoryModel CurrentModel => currentModel;
        public InventoryPanelSession Session => session;
        public int QuickMovePriority => quickMovePriority;
        public bool IsInteractionEnabled => interactionEnabled;

        /// <summary>Controls whether this panel can initiate or receive inventory operations.</summary>
        public void SetInteractionEnabled(bool enabled)
        {
            if (interactionEnabled == enabled)
                return;

            interactionEnabled = enabled;
            if (!enabled)
            {
                // A panel may become read-only while another panel is being dragged
                // over it. Cancel any drag sourced from this panel and clear transient UI.
                session?.CancelDragFrom(this);
                HideItemTooltip();
                CloseItemActionMenu();
            }
        }

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
            root.RegisterCallback<PointerDownEvent>(OnRootPointerDownForContextMenu, TrickleDown.TrickleDown);
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
            {
                HideItemTooltip();
                CancelActiveDrag();
            }
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
            ApplyDefaultObjectBorder(slot, 1f);
        }

        private void ApplyDefaultObjectBorder(VisualElement element, float width)
        {
            if (element == null) return;
            Color color = canvas != null ? canvas.DefaultObjectBorderColor : new Color(0.74f, 0.82f, 0.92f, 0.35f);
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
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
            HideItemTooltip();
            currentModel = model;
            panel.style.display = DisplayStyle.Flex;
            SetSpriteBackground(panel, currentAppearance != null ? currentAppearance.PanelBackground : null);
            CloseItemActionMenu();
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
                float configuredIconSize = ItemGridShape.GetIconSizeInCells(item.details);
                for (int shapeY = 0; shapeY < placement.Height; shapeY++)
                for (int shapeX = 0; shapeX < placement.Width; shapeX++)
                {
                    if (!ItemGridShape.IsOccupied(item.details, shapeX, shapeY, placement.RotationQuarterTurns)) continue;
                    var occupiedCell = new VisualElement();
                    occupiedCell.AddToClassList("gridItemOccupiedCell");
                    occupiedCell.userData = new Vector2Int(shapeX, shapeY);
                    ApplyDefaultObjectBorder(occupiedCell, 1f);
                    SetSpriteBackground(occupiedCell,
                        currentAppearance != null ? currentAppearance.OccupiedItemCellBackground : null);
                    occupiedCell.pickingMode = PickingMode.Position;
                    RegisterItemPointerHandler(occupiedCell, item);
                    RegisterItemHoverHandlers(occupiedCell, item, placement.RotationQuarterTurns);
                    occupiedCell.style.position = Position.Absolute;
                    occupiedCell.style.left = shapeX * gridCellSize;
                    occupiedCell.style.top = shapeY * gridCellSize;
                    occupiedCell.style.width = gridCellSize;
                    occupiedCell.style.height = gridCellSize;
                    itemElement.Insert(0, occupiedCell);
                }
                Image itemIcon = itemElement.Q<Image>(className: "slotIcon");
                if (itemIcon != null)
                {
                    // Icon sizing is a uniform scale independent from the occupied mask.
                    // Preserve the sprite's aspect ratio so it is never stretched.
                    float iconSize = configuredIconSize * gridCellSize - 4f;
                    itemIcon.scaleMode = ScaleMode.ScaleToFit;
                    itemIcon.style.position = Position.Absolute;
                    itemIcon.style.width = iconSize;
                    itemIcon.style.height = iconSize;
                    itemIcon.style.left = (placement.Width * gridCellSize - 4f - iconSize) * 0.5f;
                    itemIcon.style.top = (placement.Height * gridCellSize - 4f - iconSize) * 0.5f;
                    itemIcon.style.right = StyleKeyword.Auto;
                    itemIcon.style.bottom = StyleKeyword.Auto;
                    itemIcon.style.rotate = new Rotate(new Angle(placement.RotationDegrees, AngleUnit.Degree));
                }
                itemElement.RemoveFromClassList("slotItem");
                itemElement.AddToClassList("gridSlotItem");
                ApplyDefaultObjectBorder(itemElement, 0f);
                itemElement.style.position = Position.Absolute;
                itemElement.style.left = placement.X * gridCellSize;
                itemElement.style.top = placement.Y * gridCellSize;
                itemElement.style.width = placement.Width * gridCellSize;
                itemElement.style.height = placement.Height * gridCellSize;
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
            if (dragging) HideItemTooltip();
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
            Vector2 pivot = item.details != null ? item.details.GetRotatedDragPivot(rotationQuarterTurns) : new Vector2(0.5f, 0.5f);
            ItemGridShape.GetRotatedSize(item.details, 0, out int footprintWidth, out int footprintHeight);
            Vector2 footprintSize = new Vector2(footprintWidth * gridCellSize, footprintHeight * gridCellSize);
            float iconSize = ItemGridShape.GetIconSizeInCells(item.details) * gridCellSize;
            canvas?.ShowDragGhost(screenPosition, item.details != null ? item.details.Icon : null,
                footprintSize, iconSize, pivot, rotationQuarterTurns);
        }

        public void Clear()
        {
            HideItemTooltip();
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
            ApplyDefaultObjectBorder(element, 1f);
            if (draggingItemIds.Contains(item.Id))
                element.AddToClassList("inventory-item-dragging");
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
                countLabel.style.color = item.details != null && item.details.LabelStyle != null
                    ? item.details.LabelStyle.LabelColor : Color.white;
                element.Add(countLabel);
            }

            if (registerPointerHandler)
            {
                RegisterItemPointerHandler(element, item);
                RegisterItemHoverHandlers(element, item, 0);
            }
            return element;
        }

        // ITEM PRESS / CLICK ENTRY POINT:
        // Add item-specific pointer actions here (for example, right-click inspect or
        // modifier-click actions). Keep the actual inventory operation in the session/service
        // layer rather than putting model mutation directly in this view callback.
        private void RegisterItemPointerHandler(VisualElement target, Item item)
        {
            bool hasConfiguredActions = session != null && currentModel != null &&
                session.GetItemActions(this, item.Id).Count > 0;

            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                HideItemTooltip();
                if (session == null || currentModel == null || !interactionEnabled) return;
                if (evt.button == 1)
                {
                    // Use an explicit runtime UI Toolkit menu instead of relying on the
                    // platform contextual-menu event, which is not consistently raised
                    // for Pointer events on all runtime panels/input backends.
                    IReadOnlyList<InventoryItemAction> actions = session.GetItemActions(this, item.Id);
                    if (actions.Count > 0)
                    {
                        ShowItemActionMenu(item.Id, actions, evt.position);
                        evt.StopPropagation();
                        return;
                    }

                    CloseItemActionMenu();
                    if (!session.BeginSplitDrag(this, item.Id, evt.pointerId)) return;
                    session.UpdateDragTarget(evt.position);
                    evt.StopPropagation();
                    return;
                }
                if (evt.button != 0) return;
                if ((evt.modifiers & EventModifiers.Shift) != 0)
                {
                    CloseItemActionMenu();
                    session.QuickMove(this, item.Id);
                    evt.StopPropagation();
                    return;
                }
                CloseItemActionMenu();
                if (!session.BeginDrag(this, item.Id, evt.pointerId)) return;
                session.UpdateDragTarget(evt.position);
                evt.StopPropagation();
            });
        }

        private void RegisterItemHoverHandlers(VisualElement target, Item item, int rotationQuarterTurns)
        {
            target.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (item == null || draggingItemIds.Contains(item.Id)) return;
                bool sameItem = hoveredTooltipItem != null && Equals(hoveredTooltipItem.Id, item.Id);
                if (!sameItem)
                {
                    HideTooltipVisualOnly();
                    ClearHoveredOutline();
                    tooltipShowSchedule?.Pause();
                    hoveredTooltipItem = item;
                    hoveredTooltipRotation = rotationQuarterTurns;
                    SetHoveredOutline(item, true, rotationQuarterTurns);
                    ScheduleTooltipShow();
                }
                lastTooltipPointerPosition = evt.position;
            });
            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (hoveredTooltipItem == null || !Equals(hoveredTooltipItem.Id, item.Id)) return;
                lastTooltipPointerPosition = evt.position;
                PositionActiveTooltip();
            });
            target.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                // Grid items are composed of multiple occupied-cell elements. Moving
                // between cells belonging to the same item is still one continuous hover.
                // PointerLeaveEvent does not expose relatedTarget in all supported
                // UI Toolkit versions. Pick the element now beneath the pointer instead.
                VisualElement next = target.panel?.Pick(evt.position) as VisualElement;
                if (IsSameItemHoverTarget(next, item.Id)) return;
                if (hoveredTooltipItem == null || !Equals(hoveredTooltipItem.Id, item.Id)) return;
                HideItemTooltip();
            });
        }

        private static bool IsSameItemHoverTarget(VisualElement target, SerializableGuid itemId)
        {
            // Grid occupied cells store local coordinates in userData, while their
            // common item parent stores the item's ID. Walk upward to support both.
            for (VisualElement current = target; current != null; current = current.parent)
                if (Equals(current.userData, itemId)) return true;
            return false;
        }

        private void ScheduleTooltipShow()
        {
            tooltipShowSchedule?.Pause();
            if (root == null) return;
            tooltipShowSchedule = root.schedule.Execute(() =>
            {
                if (hoveredTooltipItem == null || draggingItemIds.Contains(hoveredTooltipItem.Id)) return;
                ShowItemTooltip(hoveredTooltipItem, hoveredTooltipRotation);
            }).StartingIn(Mathf.RoundToInt(Mathf.Max(0f, tooltipDelay) * 1000f));
        }

        private void ShowItemTooltip(Item item, int rotationQuarterTurns)
        {
            HideTooltipVisualOnly();
            if (root == null || item == null || item.details == null) return;

            var tooltip = new VisualElement { name = "inventoryItemTooltip" };
            tooltip.AddToClassList("inventoryItemTooltip");
            tooltip.pickingMode = PickingMode.Ignore;
            tooltip.style.position = Position.Absolute;
            tooltip.style.maxWidth = tooltipMaxWidth;
            tooltip.style.color = Color.white;
            tooltip.style.paddingLeft = 10f;
            tooltip.style.paddingRight = 10f;
            tooltip.style.paddingTop = 8f;
            tooltip.style.paddingBottom = 8f;
            tooltip.style.backgroundColor = new Color(0.075f, 0.085f, 0.11f, 0.97f);
            tooltip.style.borderTopWidth = 1f;
            tooltip.style.borderBottomWidth = 1f;
            tooltip.style.borderLeftWidth = 1f;
            tooltip.style.borderRightWidth = 1f;
            Color edge = new Color(0.42f, 0.48f, 0.58f, 1f);
            tooltip.style.borderTopColor = edge;
            tooltip.style.borderBottomColor = edge;
            tooltip.style.borderLeftColor = edge;
            tooltip.style.borderRightColor = edge;
            if (tooltipStyleSheet != null) tooltip.styleSheets.Add(tooltipStyleSheet);

            var title = new Label(item.details.Name);
            title.AddToClassList("inventoryItemTooltip-title");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14f;
            title.style.color = item.details.LabelStyle != null ? item.details.LabelStyle.LabelColor : Color.white;
            tooltip.Add(title);

            if (!string.IsNullOrWhiteSpace(item.details.Description))
            {
                var description = new Label(item.details.Description);
                description.AddToClassList("inventoryItemTooltip-description");
                description.style.whiteSpace = WhiteSpace.Normal;
                description.style.color = Color.white;
                description.style.marginTop = 4f;
                tooltip.Add(description);
            }

            var summary = new Label(item.quantity > 1 ? $"Quantity: {item.quantity}" : "Quantity: 1");
            summary.AddToClassList("inventoryItemTooltip-summary");
            summary.style.color = Color.white;
            summary.style.marginTop = 5f;
            tooltip.Add(summary);

            InventoryItemTooltipContext context = new InventoryItemTooltipContext(item, rotationQuarterTurns);
            var footprint = new VisualElement();
            footprint.AddToClassList("inventoryItemTooltip-footprint");
            footprint.style.flexDirection = FlexDirection.Row;
            footprint.style.alignItems = Align.Center;
            footprint.style.marginTop = 7f;
            var shape = new VisualElement();
            shape.AddToClassList("inventoryItemTooltip-shape");
            float previewCellSize = Mathf.Min(18f, 96f / Mathf.Max(context.FootprintWidth, context.FootprintHeight));
            shape.style.width = context.FootprintWidth * previewCellSize;
            shape.style.height = context.FootprintHeight * previewCellSize;
            shape.style.position = Position.Relative;
            for (int y = 0; y < context.FootprintHeight; y++)
            for (int x = 0; x < context.FootprintWidth; x++)
            {
                if (!ItemGridShape.IsOccupied(item.details, x, y, rotationQuarterTurns)) continue;
                var cell = new VisualElement();
                cell.AddToClassList("inventoryItemTooltip-shapeCell");
                cell.style.position = Position.Absolute;
                cell.style.left = x * previewCellSize;
                cell.style.top = y * previewCellSize;
                cell.style.width = Mathf.Max(2f, previewCellSize - 3f);
                cell.style.height = Mathf.Max(2f, previewCellSize - 3f);
                cell.style.backgroundColor = new Color(0.55f, 0.68f, 0.86f, 1f);
                shape.Add(cell);
            }
            footprint.Add(shape);
            var footprintText = new Label($"Space: {context.OccupiedCellCount} cell(s)  •  {context.FootprintWidth} × {context.FootprintHeight}");
            footprintText.style.marginLeft = 9f;
            footprintText.style.whiteSpace = WhiteSpace.Normal;
            footprintText.style.color = Color.white;
            footprint.Add(footprintText);
            tooltip.Add(footprint);

            if (item.details.TooltipSections != null)
            {
                foreach (InventoryItemTooltipSection section in item.details.TooltipSections)
                {
                    if (section == null) continue;
                    VisualElement customContent = section.CreateContent(context);
                    if (customContent != null)
                    {
                        customContent.AddToClassList("inventoryItemTooltip-customSection");
                        customContent.style.marginTop = 6f;
                        tooltip.Add(customContent);
                    }
                }
            }

            activeTooltip = tooltip;
            root.Add(tooltip);
            tooltip.BringToFront();
            tooltip.RegisterCallback<GeometryChangedEvent>(_ => PositionActiveTooltip());
            PositionActiveTooltip();
        }

        private void PositionActiveTooltip()
        {
            if (activeTooltip == null || root == null) return;
            Vector2 local = root.WorldToLocal(lastTooltipPointerPosition);
            float left = local.x + tooltipOffset;
            float top = local.y + tooltipOffset;
            Rect bounds = root.contentRect;
            float width = activeTooltip.layout.width > 0f ? activeTooltip.layout.width : tooltipMaxWidth;
            float height = activeTooltip.layout.height;
            if (left + width > bounds.width) left = local.x - width - tooltipOffset;
            if (top + height > bounds.height) top = Mathf.Max(0f, bounds.height - height);
            activeTooltip.style.left = Mathf.Clamp(left, 0f, Mathf.Max(0f, bounds.width - width));
            activeTooltip.style.top = Mathf.Clamp(top, 0f, Mathf.Max(0f, bounds.height - height));
        }

        private void HideItemTooltip()
        {
            tooltipShowSchedule?.Pause();
            tooltipShowSchedule = null;
            hoveredTooltipItem = null;
            HideTooltipVisualOnly();
            ClearHoveredOutline();
        }

        private void HideTooltipVisualOnly()
        {
            if (activeTooltip == null) return;
            activeTooltip.RemoveFromHierarchy();
            activeTooltip = null;
        }

        private void SetHoveredOutline(Item item, bool visible, int rotationQuarterTurns)
        {
            ClearHoveredOutline();
            if (!visible || item == null) return;
            Color resolvedOutlineColor = item.details != null && item.details.OutlineStyle != null
                ? item.details.OutlineStyle.OutlineColor
                : (canvas != null ? canvas.DefaultItemHoverOutlineColor : hoverOutlineColor);
            foreach (VisualElement itemElement in itemElements)
            {
                if (!Equals(itemElement.userData, item.Id)) continue;
                if (itemElement.ClassListContains("gridSlotItem") && item.details != null)
                {
                    foreach (VisualElement cell in itemElement.Children())
                    {
                        if (!cell.ClassListContains("gridItemOccupiedCell")) continue;
                        Vector2Int position = cell.userData is Vector2Int value ? value : Vector2Int.zero;
                        ApplyGridCellOutline(cell, item.details, position.x, position.y, rotationQuarterTurns, resolvedOutlineColor);
                        hoveredOutlineCells.Add(cell);
                    }
                }
                else
                {
                    ApplyHoverOutline(itemElement, resolvedOutlineColor);
                    hoveredOutlineCells.Add(itemElement);
                }
                break;
            }
        }

        private void ApplyGridCellOutline(VisualElement element, ItemDetails details, int x, int y, int rotationQuarterTurns, Color outlineColor)
        {
            bool top = !ItemGridShape.IsOccupied(details, x, y - 1, rotationQuarterTurns);
            bool bottom = !ItemGridShape.IsOccupied(details, x, y + 1, rotationQuarterTurns);
            bool left = !ItemGridShape.IsOccupied(details, x - 1, y, rotationQuarterTurns);
            bool right = !ItemGridShape.IsOccupied(details, x + 1, y, rotationQuarterTurns);
            element.style.borderTopWidth = top ? hoverOutlineWidth : 0f;
            element.style.borderBottomWidth = bottom ? hoverOutlineWidth : 0f;
            element.style.borderLeftWidth = left ? hoverOutlineWidth : 0f;
            element.style.borderRightWidth = right ? hoverOutlineWidth : 0f;
            element.style.borderTopColor = outlineColor;
            element.style.borderBottomColor = outlineColor;
            element.style.borderLeftColor = outlineColor;
            element.style.borderRightColor = outlineColor;
        }

        private void ApplyHoverOutline(VisualElement element, Color outlineColor)
        {
            element.style.borderTopWidth = hoverOutlineWidth;
            element.style.borderBottomWidth = hoverOutlineWidth;
            element.style.borderLeftWidth = hoverOutlineWidth;
            element.style.borderRightWidth = hoverOutlineWidth;
            element.style.borderTopColor = outlineColor;
            element.style.borderBottomColor = outlineColor;
            element.style.borderLeftColor = outlineColor;
            element.style.borderRightColor = outlineColor;
        }

        private void ClearHoveredOutline()
        {
            foreach (VisualElement element in hoveredOutlineCells)
            {
                if (element == null) continue;
                ApplyDefaultObjectBorder(element,
                    element.ClassListContains("gridItemOccupiedCell") || element.ClassListContains("slotItem") ||
                    element.ClassListContains("equipmentSlotItem") ? 1f : 0f);
            }
            hoveredOutlineCells.Clear();
        }

        private void ShowItemActionMenu(SerializableGuid itemId,
            IReadOnlyList<InventoryItemAction> actions, Vector2 pointerPosition)
        {
            CloseItemActionMenu();
            if (root == null || actions == null || actions.Count == 0) return;

            Vector2 localPosition = root.WorldToLocal(pointerPosition);
            var menu = new VisualElement { name = "inventoryItemActionMenu" };
            menu.AddToClassList("inventoryItemActionMenu");
            menu.style.position = Position.Absolute;
            menu.style.left = Mathf.Max(0f, localPosition.x);
            menu.style.top = Mathf.Max(0f, localPosition.y);
            menu.style.minWidth = 170f;
            menu.style.maxWidth = 240f;
            menu.style.paddingLeft = 5f;
            menu.style.paddingRight = 5f;
            menu.style.paddingTop = 5f;
            menu.style.paddingBottom = 5f;
            menu.style.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 0.98f);
            menu.style.borderTopWidth = 1f;
            menu.style.borderBottomWidth = 1f;
            menu.style.borderLeftWidth = 1f;
            menu.style.borderRightWidth = 1f;
            menu.style.borderTopColor = new Color(0.48f, 0.53f, 0.62f);
            menu.style.borderBottomColor = new Color(0.48f, 0.53f, 0.62f);
            menu.style.borderLeftColor = new Color(0.48f, 0.53f, 0.62f);
            menu.style.borderRightColor = new Color(0.48f, 0.53f, 0.62f);

            foreach (InventoryItemAction action in actions)
            {
                if (action == null) continue;
                InventoryItemActionAvailability availability = session.ItemActionService.GetAvailability(
                    currentModel, itemId, action);
                bool canExecute = availability.IsAvailable;
                string reason = availability.Reason;
                var actionButton = new Button(() =>
                {
                    CloseItemActionMenu();
                    session?.ExecuteItemAction(this, itemId, action);
                })
                {
                    text = action.DisplayName,
                    tooltip = canExecute ? string.Empty : reason
                };
                actionButton.SetEnabled(canExecute);
                actionButton.style.minHeight = 28f;
                actionButton.style.marginBottom = string.IsNullOrEmpty(reason) ? 2f : 0f;
                menu.Add(actionButton);
                if (!canExecute && !string.IsNullOrWhiteSpace(reason))
                {
                    var reasonLabel = new Label(reason);
                    reasonLabel.AddToClassList("inventoryItemActionReason");
                    reasonLabel.style.whiteSpace = WhiteSpace.Normal;
                    reasonLabel.style.fontSize = 10f;
                    reasonLabel.style.color = new Color(0.72f, 0.75f, 0.80f);
                    reasonLabel.style.marginLeft = 8f;
                    reasonLabel.style.marginRight = 8f;
                    reasonLabel.style.marginBottom = 5f;
                    menu.Add(reasonLabel);
                }
            }

            if (menu.childCount == 0) return;
            activeContextMenu = menu;
            root.Add(menu);
            menu.BringToFront();

            // Keep the menu inside the shared inventory canvas after its first layout.
            menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (activeContextMenu != menu || root == null) return;
                Rect bounds = root.contentRect;
                float maxLeft = Mathf.Max(0f, bounds.width - menu.layout.width);
                float maxTop = Mathf.Max(0f, bounds.height - menu.layout.height);
                menu.style.left = Mathf.Clamp(menu.layout.x, 0f, maxLeft);
                menu.style.top = Mathf.Clamp(menu.layout.y, 0f, maxTop);
            });
        }

        private void OnRootPointerDownForContextMenu(PointerDownEvent evt)
        {
            if (!interactionEnabled)
            {
                CloseItemActionMenu();
                return;
            }
            if (activeContextMenu == null) return;
            VisualElement target = evt.target as VisualElement;
            if (target != null && activeContextMenu.Contains(target)) return;
            CloseItemActionMenu();
        }

        private void CloseItemActionMenu()
        {
            if (activeContextMenu == null) return;
            activeContextMenu.RemoveFromHierarchy();
            activeContextMenu = null;
        }

        private void OnDisable()
        {
            HideItemTooltip();
            // A panel can be hidden by its host or disabled directly; either way an
            // active drag must be cancelled and its shared-canvas ghost removed.
            session?.CancelDragFrom(this);
        }

        private void OnDestroy()
        {
            if (session != null) session.Unregister(this);
            CloseItemActionMenu();
            HideItemTooltip();
            if (root != null)
            {
                root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
                root.UnregisterCallback<PointerDownEvent>(OnRootPointerDownForContextMenu, TrickleDown.TrickleDown);
            }
            ReleasePointer();
        }
    }
}
