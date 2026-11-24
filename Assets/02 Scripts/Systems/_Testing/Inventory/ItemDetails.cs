using System;
using System.Collections.Generic;
using UnityEngine;
using Systems.Inventory.Architecture;
    
namespace Systems.Inventory {
    [CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
    [Serializable]
    public class ItemDetails : ScriptableObject {

        
        public string Name;

        [Header("Classification")]
        public ItemCategory category = ItemCategory.General;
        public EquipmentType equipmentType = EquipmentType.None;

        [Header("Equipment Effects")]
        [Tooltip("Attribute modifiers applied while an instance of this item is equipped.")]
        public List<EquipmentEffectDefinition> EquipmentEffects = new List<EquipmentEffectDefinition>();

        
        [Tooltip("Maximum quantity in one stack. Applies to equipment too; equipment defaults to 1 unless explicitly increased.")]
        [Min(1)] public int maxStack = 1;

        [Min(1)] public int GridWidth = 1;
        [Min(1)] public int GridHeight = 1;

        [Header("Grid Shape")]
        [Tooltip("When enabled, only painted cells occupy inventory space. When disabled, the full rectangle is used.")]
        public bool UseCustomShape;
        [HideInInspector] public List<bool> ShapeCells = new List<bool> { true };

        [Header("Drag Visual")]
        [Tooltip("Normalized point on the item visual that stays under the pointer while dragging. (0,0) is top-left; (1,1) is bottom-right.")]
        public Vector2 DragPivot = new Vector2(0.5f, 0.5f);
            
        
        public SerializableGuid Id = SerializableGuid.NewGuid();

        
        private void OnValidate() {
            maxStack = Mathf.Max(1, maxStack);
            GridWidth = Mathf.Clamp(GridWidth, 1, 16);
            GridHeight = Mathf.Clamp(GridHeight, 1, 16);
            if (ShapeCells == null) ShapeCells = new List<bool>();
            int requiredCells = GridWidth * GridHeight;
            while (ShapeCells.Count < requiredCells) ShapeCells.Add(true);
            while (ShapeCells.Count > requiredCells) ShapeCells.RemoveAt(ShapeCells.Count - 1);
            DragPivot = new Vector2(Mathf.Clamp01(DragPivot.x), Mathf.Clamp01(DragPivot.y));
        }

        private void AssignNewGuid() {
            Id = SerializableGuid.NewGuid();
        }
        
       
        public Sprite Icon;
        
        
        public string Description;
        
        public Item Create(int quantity) {
            return new Item(this, quantity);
        }
    }
}