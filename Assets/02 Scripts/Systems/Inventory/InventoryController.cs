using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Systems.Inventory {
    public class ViewModel {
        public readonly int Capacity;
        public readonly BindableProperty<string> Coins;

        public ViewModel(InventoryModel model, int capacity) {
            Capacity = capacity;
            Coins = BindableProperty<string>.Bind(() => model.Coins.ToString());
        }
    }
    
    public class InventoryController { 
        readonly InventoryView view;
        public InventoryModel model {get; private set;}
        readonly int capacity;
        public SerializableGuid controlID; 

        // Constructor, called by the builder, from Inventory
        InventoryController(InventoryView view, int capacity, InventoryModel initialModel = null) {
            Debug.Assert(view != null, "View is null");
            //Debug.Assert(model != null, "Model is null");
            Debug.Assert(capacity > 0, "Capacity is less than 1");
            controlID = SerializableGuid.NewGuid();
            this.view = view;
            this.view.InitializeControllerRef(this); // give it a ref for this controller.
            this.capacity = capacity;

            //model = initialModel;
            if (initialModel != null) {
                Debug.Log("Model Bound");
                BindModel(initialModel);
            }

            view.StartCoroutine(Initialize());

            
        }
        
        public void Bind(InventoryData data) => model.Bind(data);

        public void BindModel(InventoryModel newModel) {
            // 1. Unsubscribe from the previous chest/container to prevent memory leaks
            if (model != null) {
                model.OnModelChanged -= HandleModelChanged;
            }

            model = newModel;

            // 2. Subscribe to the new container and force an immediate UI refresh
            if (model != null) {
                model.OnModelChanged += HandleModelChanged;
                //RefreshView();
            }
        }
        public void UnbindModel() => BindModel(null);

        public void AddCoins(int amount) => model.AddCoins(amount);

        IEnumerator Initialize() {
            yield return view.InitializeView(new ViewModel(model, capacity));
            
            model.AddCoins(10);
            
            view.OnDrop += HandleDrop;
            
            //pass this as the refrence controller to all the slots
            foreach(var slot in view.Slots)
            {
                slot.AssignController(this);
            }

            RefreshView();
        }

        void HandleDrop(Slot originalSlot, Slot closestSlot) {
            // Moving to Same Slot or Empty Slot
            if(originalSlot.controlRef.controlID == closestSlot.controlRef.controlID){
                if (originalSlot.Index == closestSlot.Index || closestSlot.ItemId.Equals(SerializableGuid.Empty)) {
                    model.Swap(originalSlot.Index, closestSlot.Index);
                    return;
                }
        
                // Moving to Non-Empty Slot
                var sourceItemId = model.Get(originalSlot.Index).details.Id;
                var targetItemId = model.Get(closestSlot.Index).details.Id;
                            
                if (sourceItemId.Equals(targetItemId) && model.Get(closestSlot.Index).details.maxStack > 1) { 
                    model.Combine(originalSlot.Index, closestSlot.Index);
                } else {
                    model.Swap(originalSlot.Index, closestSlot.Index);
                }
            }
            // TODO world drops
            // TODO Cross Inventory drops
            // TODO Hotbar drops
        }

        void HandleModelChanged(IList<Item> items) => RefreshView();
        
        void RefreshView() {
            if(model == null) return;
            for (int i = 0; i < capacity; i++) {
                var item = model.Get(i);
                if (item == null || item.Id.Equals(SerializableGuid.Empty)) {
                    view.Slots[i].Set(SerializableGuid.Empty, null);
                } else {
                    view.Slots[i].Set(item.Id, item.details.Icon, item.quantity);
                    // Uses the items unique id instead of the Item Details ID.
                }
            }
        }
        
        #region Builder
        
        public class Builder {
            InventoryView view;
            IEnumerable<ItemDetails> itemDetails;
            int capacity = 20;
            
            public Builder(InventoryView view) {
                this.view = view;
            }

            public Builder WithStartingItems(IEnumerable<ItemDetails> itemDetails) {
                this.itemDetails = itemDetails;
                return this;
            }

            public Builder WithCapacity(int capacity) {
                this.capacity = capacity;
                return this;
            }

            public InventoryController Build() {
                InventoryModel model = itemDetails != null 
                    ? new InventoryModel(itemDetails, capacity) 
                    : new InventoryModel(Array.Empty<ItemDetails>(), capacity);

                return new InventoryController(view, capacity, model);
            }
        }
        
        #endregion Builder
    }
}