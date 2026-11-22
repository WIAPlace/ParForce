using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory {
    public abstract class StorageView : MonoBehaviour {
        protected List<Slot> slotPool = new();
        InventoryController controller;

        public IReadOnlyList<Slot> Slots => slotPool;

        [SerializeField] protected UIDocument document;
        [SerializeField] protected StyleSheet styleSheet;

        //public uint modelID;

        protected static VisualElement ghostIcon;

        static bool isDragging;
        static Slot originalSlot;
        
        static protected VisualElement root;
        protected VisualElement container;
        protected VisualElement slotsContainer;
        
        public event Action<Slot, Slot> OnDrop;
        
        void Start() {
            ghostIcon.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            ghostIcon.RegisterCallback<PointerUpEvent>(OnPointerUp);
            
            foreach (var slot in Slots) {
                slot.OnStartDrag += OnPointerDown;
            }
        }

        public abstract IEnumerator InitializeView(ViewModel viewModel);

        public void InitializeControllerRef(InventoryController controller)
        {
            this.controller=controller;
        }

        static void OnPointerDown(Vector2 position, Slot slot) {
            isDragging = true;
            originalSlot = slot;
        
            SetGhostIconPosition(position);
            
            ghostIcon.style.backgroundImage = originalSlot.BaseSprite.texture;
            originalSlot.Icon.image = null;
            originalSlot.StackLabel.visible = false;
            
            ghostIcon.style.visibility = Visibility.Visible;
            // TODO show stack size on ghost icon
        }
        
        void OnPointerMove(PointerMoveEvent evt) {
            if (!isDragging) return;
            
            SetGhostIconPosition(evt.position);
        }

        /*
        void OnPointerUp(PointerUpEvent evt) {
            if (!isDragging) return;
            Slot closestSlot = Slots
                .Where(slot => slot.worldBound.Overlaps(ghostIcon.worldBound))
                .OrderBy(slot => Vector2.Distance(slot.worldBound.position, ghostIcon.worldBound.position))
                .FirstOrDefault();
        
            if (closestSlot != null) {
                OnDrop?.Invoke(originalSlot, closestSlot);
            } else {
                originalSlot.Icon.image = originalSlot.BaseSprite.texture; 
            }
            
            isDragging = false;
            originalSlot = null;
            ghostIcon.style.visibility = Visibility.Hidden;
        }
        */

        static void OnPointerUp(PointerUpEvent evt) {
            if (!isDragging) return;

            // 1. Pick across the ENTIRE UI panel so it finds slots in ANY open inventory
            VisualElement picked = root.panel.Pick(evt.position);
            Slot closestSlot = picked as Slot ?? picked?.GetFirstAncestorOfType<Slot>();

            // 2. If dropped on a valid, different slot, run your cross-inventory swap/transfer
            if (closestSlot != null && closestSlot != originalSlot) {
                //Container.TransferOrSwap(originalSlot, closestSlot);
            } 
            // 3. Otherwise, restore the original slot's icon visibility (so the item doesn't vanish!)
            else if (originalSlot != null) {
                originalSlot.Icon.image = originalSlot.BaseSprite.texture;
            }

            // 4. Clean up drag state
            isDragging = false;
            originalSlot = null;
            ghostIcon.style.visibility = Visibility.Hidden;
        }

        static void SetGhostIconPosition(Vector2 position) {
            ghostIcon.style.top = position.y - ghostIcon.layout.height / 2;
            ghostIcon.style.left = position.x - ghostIcon.layout.width / 2;
        }
        
        void OnDestroy() {
            foreach (var slot in Slots) {
                slot.OnStartDrag -= OnPointerDown;
            }
        }

        public void ConfigureForCapacity(int capacity) {
            // 1. Grow the pool if this container is bigger than any we've opened before
            while (slotPool.Count < capacity) {
                var newSlot = new Slot();
                newSlot.AssignController(controller);
                slotsContainer.Add(newSlot);
                slotPool.Add(newSlot);
            }

            // 2. Show active slots and hide unused ones (zero allocations!)
            for (int i = 0; i < slotPool.Count; i++) {
                bool isActive = i < capacity;
                slotPool[i].style.display = isActive ? DisplayStyle.Flex : DisplayStyle.None;
                
                if (!isActive) {
                    slotPool[i].Set(null); // Clear stale icons on hidden slots
                }
            }
        }
    }
}