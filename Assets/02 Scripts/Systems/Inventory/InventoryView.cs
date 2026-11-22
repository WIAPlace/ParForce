using System.Collections;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory {
    public class InventoryView : StorageView {
        [SerializeField] string panelName = "Inventory";

        public override IEnumerator InitializeView(ViewModel viewModel) {
            slotPool = new List<Slot>(viewModel.Capacity);
            slotPool.Clear();
            root = document.rootVisualElement;
            root.Clear(); 

            root.styleSheets.Add(styleSheet);
 
            container = root.CreateChild("container");
            
            var inventory = container.CreateChild("inventory").WithManipulator(new PanelDragManipulator());
            inventory.CreateChild("inventoryFrame");
            inventory.CreateChild("inventoryHeader").Add(new Label(panelName));

            //Debug.Log(slotPool.Capacity);
            slotsContainer = inventory.CreateChild("slotsContainer");
            for (int i = 0; i < viewModel.Capacity; i++) {
                //Debug.Log(i);
                var slot = slotsContainer.CreateChild<Slot>("slot");
                slotPool.Add(slot);
            }
            
            var coins = inventory.CreateChild("coins");
            var coinsLabel = new Label();
            coins.CreateChild("coinsIcon");
            coins.Add(coinsLabel);
            coins.dataSource = viewModel.Coins;
            
            coinsLabel.SetBinding(nameof(Label.text), new DataBinding {
                dataSourcePath = new PropertyPath(nameof(BindableProperty<string>.Value)),
                bindingMode = BindingMode.ToTarget
            });
            
            ghostIcon = container.CreateChild("ghostIcon");
            
            yield return null; 
        }
    }
}