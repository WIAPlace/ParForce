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

        [Header("Item Actions")]
        [Tooltip("Actions shown in the item context menu. Actions are ScriptableObject definitions and should be stateless.")]
        public List<InventoryItemAction> Actions = new List<InventoryItemAction>();

        [Header("Tooltip")]
        [Tooltip("Optional custom UI sections appended to the standard item tooltip.")]
        public List<InventoryItemTooltipSection> TooltipSections = new List<InventoryItemTooltipSection>();

        [Tooltip("Optional reusable color style for this item's displayed name and stack label. White is used when unset.")]
        public InventoryItemLabelStyle LabelStyle;

        [Header("Equipment Effects")]
        [Tooltip("Attribute modifiers applied while an instance of this item is equipped.")]
        public List<EquipmentEffectDefinition> EquipmentEffects = new List<EquipmentEffectDefinition>();

        [Header("World Visual")]
        [Tooltip("Optional local-only visual prefab instantiated beneath a NetworkWorldItem's Visual Root. It must not contain a NetworkObject. Enabled Collider components on active children become the item's local pickup collider(s); MeshColliders without a mesh and TerrainColliders without terrain data are ignored. When no usable visual colliders exist, the NetworkWorldItem root collider is used. When unset, the item Icon is used as a SpriteRenderer fallback.")]
        public GameObject WorldVisualPrefab;
        [Tooltip("Local position offset applied to the world visual prefab.")]
        public Vector3 WorldVisualLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation applied to the world visual prefab.")]
        public Vector3 WorldVisualLocalEulerAngles = Vector3.zero;
        [Tooltip("Uniform scale multiplier applied on top of the visual prefab root's authored scale.")]
        [Min(0.01f)] public float WorldVisualScale = 1f;

        
        [Tooltip("Maximum quantity in one stack. Applies to equipment too; equipment defaults to 1 unless explicitly increased.")]
        [Min(1)] public int maxStack = 1;

        [Min(1)] public int GridWidth = 1;
        [Min(1)] public int GridHeight = 1;

        [Header("Grid Shape")]
        [Tooltip("When enabled, only painted cells occupy inventory space. When disabled, the full rectangle is used.")]
        public bool UseCustomShape;
        [HideInInspector] public List<bool> ShapeCells = new List<bool> { true };

        [Header("Drag Visual")]
        [Tooltip("Normalized point on the item visual that stays under the pointer while dragging. The default (0.5, 0.5) is the visual center. (0,0) is top-left; (1,1) is bottom-right.")]
        public Vector2 DragPivot = new Vector2(0.5f, 0.5f);

        [Header("Item Outline")]
        [Tooltip("Optional reusable hover-outline color. When unset, the InventoryPanelCanvas default is used.")]
        public InventoryItemOutlineStyle OutlineStyle;

        /// <summary>Returns the normalized pivot position after clockwise quarter-turn rotation.</summary>
        public Vector2 GetRotatedDragPivot(int quarterTurns)
        {
            Vector2 pivot = DragPivot;
            int turns = ((quarterTurns % 4) + 4) % 4;
            for (int i = 0; i < turns; i++)
                pivot = new Vector2(1f - pivot.y, pivot.x);
            return pivot;
        }
            
        
        public SerializableGuid Id = SerializableGuid.NewGuid();

        [Header("Networking")]
        [Tooltip("Stable compact ID used by the multiplayer item catalog. Zero is reserved as unassigned. Assign and validate IDs through a Network Item Definition Catalog; do not change an assigned ID without coordinating a network/content migration.")]
        public ushort NetworkDefinitionId;

        
        private void OnValidate() {
            maxStack = Mathf.Max(1, maxStack);
            GridWidth = Mathf.Clamp(GridWidth, 1, 16);
            GridHeight = Mathf.Clamp(GridHeight, 1, 16);
            if (ShapeCells == null) ShapeCells = new List<bool>();
            int requiredCells = GridWidth * GridHeight;
            while (ShapeCells.Count < requiredCells) ShapeCells.Add(true);
            while (ShapeCells.Count > requiredCells) ShapeCells.RemoveAt(ShapeCells.Count - 1);
            DragPivot = new Vector2(Mathf.Clamp01(DragPivot.x), Mathf.Clamp01(DragPivot.y));
            WorldVisualScale = Mathf.Max(0.01f, WorldVisualScale);
        }

        private void AssignNewGuid() {
            Id = SerializableGuid.NewGuid();
        }
        
       
        [Header("Icon Display")]
        [Tooltip("Uniform icon size in grid-cell units. Set to 0 to automatically size from the longest dimension of the occupied footprint. The icon preserves its sprite aspect ratio.")]
        public float IconSizeInCells = 0f;

        public Sprite Icon;
        
        
        public string Description;
        
        public Item Create(int quantity) {
            return new Item(this, quantity);
        }
    }
}