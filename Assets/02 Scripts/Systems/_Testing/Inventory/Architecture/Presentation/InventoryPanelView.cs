using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>UI Toolkit presentation for one indexed-slot inventory panel.</summary>
    public sealed class InventoryPanelView : MonoBehaviour, IInventoryPanelView
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private StyleSheet interactionStyleSheet;
        [SerializeField] private InventoryPanelSession session;
        [SerializeField] private Vector2 panelPosition = new Vector2(24f, 24f);

        private VisualElement root;
        private VisualElement panel;
        private VisualElement slotsContainer;
        private VisualElement dragGhost;
        private readonly List<VisualElement> slotElements = new List<VisualElement>();
        private readonly List<VisualElement> itemElements = new List<VisualElement>();
        private IInventoryModel currentModel;
        private int capturedPointerId = -1;

        public IInventoryModel CurrentModel => currentModel;

        public void Initialize()
        {
            if (document == null)
                throw new InvalidOperationException($"{name}: UIDocument is not assigned.");

            root = document.rootVisualElement;
            root.Clear();
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (interactionStyleSheet != null) root.styleSheets.Add(interactionStyleSheet);
            root.AddToClassList("inventory-panel-root");

            panel = new VisualElement();
            panel.AddToClassList("inventory");
            panel.style.position = Position.Absolute;
            panel.style.left = panelPosition.x;
            panel.style.top = panelPosition.y;
            root.Add(panel);

            slotsContainer = new VisualElement();
            slotsContainer.AddToClassList("slotsContainer");
            panel.Add(slotsContainer);

            dragGhost = new VisualElement();
            dragGhost.AddToClassList("inventoryDragGhost");
            dragGhost.pickingMode = PickingMode.Ignore;
            root.Add(dragGhost);

            root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            if (session != null) session.Register(this);
            else Debug.LogWarning($"{name}: No InventoryPanelSession assigned; drag-and-drop is unavailable.", this);
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
            }
            header.Clear();
            header.Add(new Label(displayName));
        }

        public void Render(IInventoryModel model)
        {
            if (root == null) Initialize();
            currentModel = model;
            slotsContainer.Clear();
            slotElements.Clear();
            itemElements.Clear();
            if (model == null) { SetHeader("Inventory"); return; }
            SetHeader(model.DisplayName);

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

        public void Clear()
        {
            currentModel = null;
            slotsContainer?.Clear();
            slotElements.Clear();
            itemElements.Clear();
            dragGhost?.RemoveFromClassList("visible");
            SetHeader("Inventory");
        }

        // UIDocuments each span the whole screen. Hit-test the visible panel only.
        public bool ContainsScreenPoint(Vector2 screenPosition) =>
            panel != null && panel.worldBound.Contains(screenPosition);

        public int GetSlotIndexAt(Vector2 screenPosition)
        {
            for (int i = slotElements.Count - 1; i >= 0; i--)
                if (slotElements[i].worldBound.Contains(screenPosition)) return i;
            return -1;
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
            var icon = new Image();
            icon.AddToClassList("slotIcon");
            if (item.details != null && item.details.Icon != null)
            {
                icon.sprite = item.details.Icon;
                icon.scaleMode = ScaleMode.ScaleToFit;
            }
            else icon.AddToClassList("slotIconMissing");
            element.Add(icon);

            if (item.quantity > 1)
            {
                var quantity = new Label(item.quantity.ToString());
                quantity.AddToClassList("stackCount");
                element.Add(quantity);
            }

            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || session == null || currentModel == null) return;
                if (!session.BeginDrag(this, item.Id, evt.pointerId)) return;
                element.AddToClassList("inventory-item-dragging");
                evt.StopPropagation();
            });
            return element;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (session == null || !session.IsDraggingFrom(this)) return;
            dragGhost?.AddToClassList("visible");
            if (dragGhost != null)
            {
                Vector2 local = root.WorldToLocal(evt.position);
                dragGhost.style.left = local.x - 24;
                dragGhost.style.top = local.y - 24;
            }
            session.UpdateDragTarget(evt.position);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (session == null || !session.IsDraggingFrom(this)) return;
            session.EndDrag(evt.position, evt.pointerId);
            ReleasePointer();
            dragGhost?.RemoveFromClassList("visible");
            RemoveDraggingStyle();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (session == null || !session.IsDraggingFrom(this)) return;
            session.CancelDrag();
            ReleasePointer();
            dragGhost?.RemoveFromClassList("visible");
            RemoveDraggingStyle();
        }

        private void RemoveDraggingStyle()
        {
            foreach (VisualElement element in itemElements)
                element.RemoveFromClassList("inventory-item-dragging");
        }

        private void OnDestroy()
        {
            if (session != null) session.Unregister(this);
            ReleasePointer();
        }
    }
}
