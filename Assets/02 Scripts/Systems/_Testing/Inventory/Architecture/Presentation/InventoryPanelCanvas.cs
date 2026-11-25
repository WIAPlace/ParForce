using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Shared UI Toolkit surface for all simultaneously visible inventory panels.
    /// Create one canvas per inventory screen and point every InventoryPanelView at it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryPanelCanvas : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private StyleSheet interactionStyleSheet;
        [SerializeField] private InventoryPanelSession session;
        [Header("Global UI Scale")]
        [SerializeField, Range(0.5f, 2f), Tooltip("Shared scale applied to every inventory panel and the drag ghost.")]
        private float globalScale = 1f;
        [Header("Global Object Styling")]
        [SerializeField, Tooltip("Default border color used by inventory slots and item visuals across every panel on this canvas.")]
        private Color defaultObjectBorderColor = new Color(0.41f, 0.45f, 0.51f, 1f);
        [SerializeField, Tooltip("Default hover-outline color used when an item has no InventoryItemOutlineStyle assigned.")]
        private Color defaultItemHoverOutlineColor = new Color(0.92f, 0.82f, 0.42f, 1f);

        private readonly List<InventoryPanelView> registeredViews = new List<InventoryPanelView>();

        private VisualElement root;
        private VisualElement dragGhost;
        private Image dragGhostImage;
        private bool initialized;

        // TEMPORARY PANEL TESTING CONTROLS: set false to restore static panel positions
        // and disable click/hover focus ordering once the final layout is established.
        private const bool EnableTemporaryPanelTestingControls = true;

        public VisualElement Root => root;
        public InventoryPanelSession Session => session;
        public float GlobalScale => globalScale;
        public Color DefaultObjectBorderColor => defaultObjectBorderColor;
        public Color DefaultItemHoverOutlineColor => defaultItemHoverOutlineColor;

        /// <summary>Raised whenever the shared inventory UI scale changes.</summary>
        public event Action<float> GlobalScaleChanged;

        /// <summary>Changes the scale of every registered inventory panel and the shared drag ghost.</summary>
        public void SetGlobalScale(float scale)
        {
            float clamped = Mathf.Clamp(scale, 0.5f, 2f);
            if (Mathf.Approximately(globalScale, clamped)) return;
            globalScale = clamped;
            ApplyGlobalScale();
            GlobalScaleChanged?.Invoke(globalScale);
        }

        private void ApplyGlobalScale()
        {
            for (int i = registeredViews.Count - 1; i >= 0; i--)
            {
                if (registeredViews[i] == null) registeredViews.RemoveAt(i);
                else registeredViews[i].ApplyGlobalScale(globalScale);
            }
            ApplyScaleToElement(dragGhost, globalScale);
        }

        private static void ApplyScaleToElement(VisualElement element, float scale)
        {
            if (element == null) return;
            element.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f), 0f);
            element.style.scale = new Scale(new Vector2(scale, scale));
        }

        public void Initialize()
        {
            if (initialized) return;
            if (document == null) document = GetComponent<UIDocument>();
            if (document == null)
                throw new System.InvalidOperationException($"{name}: InventoryPanelCanvas requires a UIDocument.");

            globalScale = Mathf.Clamp(globalScale, 0.5f, 2f);
            root = document.rootVisualElement;
            root.Clear();
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (interactionStyleSheet != null) root.styleSheets.Add(interactionStyleSheet);
            root.AddToClassList("inventory-panel-root");
            root.style.flexGrow = 1;
            root.focusable = true;

            dragGhost = new VisualElement { name = "sharedInventoryDragGhost" };
            dragGhost.AddToClassList("inventoryDragGhost");
            dragGhost.pickingMode = PickingMode.Ignore;
            dragGhost.style.display = DisplayStyle.None;
            dragGhostImage = new Image { name = "dragGhostImage", scaleMode = ScaleMode.ScaleToFit };
            dragGhostImage.pickingMode = PickingMode.Ignore;
            dragGhostImage.style.position = Position.Absolute;
            dragGhostImage.style.left = 0;
            dragGhostImage.style.top = 0;
            dragGhostImage.style.right = 0;
            dragGhostImage.style.bottom = 0;
            dragGhostImage.style.width = Length.Percent(100f);
            dragGhostImage.style.height = Length.Percent(100f);
            dragGhost.Add(dragGhostImage);
            root.Add(dragGhost); // Kept last so it is always above every panel.
            ApplyScaleToElement(dragGhost, globalScale);

            root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            root.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            initialized = true;
        }

        public void Register(InventoryPanelView view)
        {
            Initialize();
            if (!registeredViews.Contains(view)) registeredViews.Add(view);
            view.AttachToCanvas(this, root);
            view.ApplyGlobalScale(globalScale);
        }

        public void ShowDragGhost(Vector2 screenPosition, Sprite sprite, Vector2 size, Vector2 pivot, int rotationQuarterTurns)
        {
            if (!initialized) Initialize();
            Vector2 local = root.WorldToLocal(screenPosition);
            float imageWidth = Mathf.Max(24f, size.x);
            float imageHeight = Mathf.Max(24f, size.y);
            int normalizedRotation = ((rotationQuarterTurns % 4) + 4) % 4;
            bool swapsFootprint = normalizedRotation % 2 != 0;
            float boundsWidth = swapsFootprint ? imageHeight : imageWidth;
            float boundsHeight = swapsFootprint ? imageWidth : imageHeight;
            dragGhost.style.width = boundsWidth;
            dragGhost.style.height = boundsHeight;
            dragGhost.style.left = local.x - boundsWidth * Mathf.Clamp01(pivot.x);
            dragGhost.style.top = local.y - boundsHeight * Mathf.Clamp01(pivot.y);
            dragGhostImage.sprite = sprite;
            dragGhostImage.style.left = (boundsWidth - imageWidth) * 0.5f;
            dragGhostImage.style.top = (boundsHeight - imageHeight) * 0.5f;
            dragGhostImage.style.right = StyleKeyword.Auto;
            dragGhostImage.style.bottom = StyleKeyword.Auto;
            dragGhostImage.style.width = imageWidth;
            dragGhostImage.style.height = imageHeight;
            dragGhostImage.style.rotate = new Rotate(new Angle(normalizedRotation * 90f, AngleUnit.Degree));
            dragGhostImage.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            dragGhost.style.display = DisplayStyle.Flex;
            dragGhost.BringToFront();
        }

        public void HideDragGhost()
        {
            if (dragGhost != null) dragGhost.style.display = DisplayStyle.None;
        }

        public void BringDragGhostToFront() => dragGhost?.BringToFront();

        // TEMPORARY PANEL TESTING CONTROLS: all panel movement/focus wiring is gated here.
        // Disabling the constant above removes the header drag manipulator and click-to-front.
        public void ConfigureTemporaryPanelTestingControls(VisualElement panel, VisualElement header, Action bringPanelToFront)
        {
            if (!EnableTemporaryPanelTestingControls || panel == null || header == null) return;
            header.AddManipulator(new PanelDragManipulator(panel, bringPanelToFront, () => GlobalScale));
            panel.RegisterCallback<PointerDownEvent>(_ => bringPanelToFront?.Invoke());
        }

        public void BringPanelToFront(VisualElement panel)
        {
            if (!EnableTemporaryPanelTestingControls || panel == null) return;
            panel.BringToFront();
            BringDragGhostToFront();
        }

        public void Focus() => root?.Focus();

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (session == null || !session.IsDragging) return;
            session.UpdateDragTarget(evt.position);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (session == null || !session.IsDragging) return;
            if (evt.button == 1)
            {
                if (!session.ConsumeSplitInitiationRelease(evt.button))
                    session.PlaceOneAt(evt.position);
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0) return;
            session.EndDrag(evt.position, evt.pointerId);
            HideDragGhost();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (session == null || !session.IsDragging) return;
            session.CancelDrag();
            HideDragGhost();
        }

        // Central keyboard shortcut handler for inventory interactions.
        // R rotates clockwise; Shift+R rotates counter-clockwise; Q drops the held item.
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (session == null || !session.IsDragging) return;

            if (evt.keyCode == KeyCode.Q)
            {
                if (session.DropDraggedItem())
                    HideDragGhost();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode != KeyCode.R) return;
            bool shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            session.RotateDraggedItem(shiftHeld ? -1 : 1);
            evt.StopPropagation();
        }

        private void OnDestroy()
        {
            if (session != null && session.IsDragging) session.CancelDrag();
            if (root == null) return;
            root.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            root.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            root.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }
    }
}
