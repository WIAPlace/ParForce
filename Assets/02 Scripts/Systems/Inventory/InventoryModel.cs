using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using UnityEngine;

namespace Systems.Inventory {
    public class InventoryModel {  // will likeley want to make some sort of interface, to distingush between diffrent model types, equipment vs inventory.
        ObservableArray<Item> Items { get; }
        InventoryData inventoryData = new InventoryData();
        readonly int capacity;

        public int Coins {
            get => inventoryData.Coins;
            set => inventoryData.Coins = value;
        }

        public event Action<Item[]> OnModelChanged {
            add => Items.AnyValueChanged += value;
            remove => Items.AnyValueChanged -= value;
        }
        
        public InventoryModel(IEnumerable<ItemDetails> itemDetails, int capacity) {
            this.capacity = capacity;
            Items = new ObservableArray<Item>(capacity);
            foreach (var itemDetail in itemDetails) {
                Items.TryAdd(itemDetail.Create(1));
            }
        }

        

        // Basicly is only called by save and load system. 
        public void Bind(InventoryData data) {
            //Debug.Log("Inventory Model Bind went Off");
            inventoryData = data;
            inventoryData.Capacity = capacity;
            
            bool isNew = inventoryData.Items == null || inventoryData.Items.Length == 0;

            if (isNew) {
                inventoryData.Items = new Item[capacity];
            }
            else {
                for (var i = 0; i < capacity; i++) {
                    if (Items[i] == null) continue;
                    inventoryData.Items[i].details = ItemDatabase.GetDetailsById(Items[i].detailsId);
                }
            }

            if (isNew && Items.Count != 0) {
                for (var i = 0; i < capacity; i++) {
                    if (Items[i] == null) continue;
                    inventoryData.Items[i] = Items[i];
                }
            }
            
            Items.items = inventoryData.Items;
        }
        
        public void AddCoins(int amount) => Coins += amount;
        
        public Item Get(int index) => Items[index];
        public void Clear() => Items.Clear();
        public bool Add(Item item) => Items.TryAdd(item);
        public bool AddAt(int index,Item item) => Items.TryAddAt(index,item);
        public bool Remove(Item item) => Items.TryRemove(item);
        public bool RemoveAt(int index) => Items.TryRemoveAt(index);
        
        public void Swap(int source, int target) => Items.Swap(source, target);
        
        public int Combine(int source, int target) {
            //Debug.Log("Comined");
            var total = Items[source].quantity + Items[target].quantity;
            Items[target].quantity = total;
            Remove(Items[source]);
            return total;
        }
    }
}