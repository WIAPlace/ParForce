using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory
{
    /// <summary>
    /// Temporary testing manipulator. Drag the supplied panel by its header and keep
    /// the complete panel inside its parent viewport.
    /// </summary>
    public sealed class PanelDragManipulator : PointerManipulator
    {
        private readonly VisualElement panel;
        private readonly Action onBeginDrag;
        private readonly Func<float> getVisualScale;
        private bool isDragging;
        private int activePointerId = -1;
        private Vector2 pointerStart;
        private Vector2 translateStart;

        public PanelDragManipulator(VisualElement panel, Action onBeginDrag)
            : this(panel, onBeginDrag, () => 1f)
        {
        }

        /// <summary>
        /// Creates a drag manipulator whose boundary calculations account for the
        /// panel's visual scale. The two-argument overload remains available for
        /// existing callers that use unscaled panels.
        /// </summary>
        public PanelDragManipulator(VisualElement panel, Action onBeginDrag, Func<float> getVisualScale)
        {
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.onBeginDrag = onBeginDrag;
            this.getVisualScale = getVisualScale ?? (() => 1f);
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt) || isDragging || panel.parent == null) return;

            isDragging = true;
            activePointerId = evt.pointerId;
            // Pointer events report world coordinates; panel translation is relative to
            // its parent. Convert once so drag deltas and screen bounds use the same space.
            pointerStart = panel.parent.WorldToLocal(evt.position);
            Translate current = panel.resolvedStyle.translate;
            translateStart = new Vector2(current.x.value, current.y.value);
            onBeginDrag?.Invoke();
            target.CapturePointer(activePointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isDragging || evt.pointerId != activePointerId || !target.HasPointerCapture(activePointerId)) return;

            Vector2 pointerPosition = panel.parent.WorldToLocal(evt.position);
            Vector2 delta = pointerPosition - pointerStart;
            Vector2 desiredTranslation = translateStart + delta;
            Rect viewport = panel.parent.contentRect;
            Rect panelRect = panel.layout;

            // Clamp the panel's final rectangle against the shared canvas viewport.
            // The panel's own dimensions only determine how much room remains at an edge;
            // all panels use the exact same screen/canvas bounds and coordinate space.
            float desiredLeft = panelRect.x + desiredTranslation.x;
            float desiredTop = panelRect.y + desiredTranslation.y;
            // UI Toolkit transforms change the panel's visual footprint without changing
            // panel.layout. Use the current visual scale when calculating the far edges,
            // otherwise the panel can overrun the viewport (or stop short) as scale changes.
            float visualScale = Mathf.Max(0.01f, getVisualScale());
            float visualWidth = panelRect.width * visualScale;
            float visualHeight = panelRect.height * visualScale;
            float maxLeft = Mathf.Max(0f, viewport.width - visualWidth);
            float maxTop = Mathf.Max(0f, viewport.height - visualHeight);
            float clampedLeft = Mathf.Clamp(desiredLeft, 0f, maxLeft);
            float clampedTop = Mathf.Clamp(desiredTop, 0f, maxTop);
            Vector2 clampedTranslation = new Vector2(
                clampedLeft - panelRect.x,
                clampedTop - panelRect.y);

            panel.style.translate = new Translate(clampedTranslation.x, clampedTranslation.y);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!isDragging || evt.pointerId != activePointerId || !CanStopManipulation(evt)) return;
            StopDragging();
            evt.StopPropagation();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!isDragging || evt.pointerId != activePointerId) return;
            StopDragging();
        }

        private void StopDragging()
        {
            if (target != null && activePointerId >= 0 && target.HasPointerCapture(activePointerId))
                target.ReleasePointer(activePointerId);
            activePointerId = -1;
            isDragging = false;
        }
    }
}
