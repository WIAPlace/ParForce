using UnityEditor;
using UnityEngine;

namespace Systems.Inventory.Editor
{
    [CustomEditor(typeof(ItemDetails))]
    public sealed class ItemDetailsEditor : UnityEditor.Editor
    {
        private SerializedProperty widthProperty;
        private SerializedProperty heightProperty;
        private SerializedProperty useCustomShapeProperty;
        private SerializedProperty shapeCellsProperty;

        private void OnEnable()
        {
            widthProperty = serializedObject.FindProperty("GridWidth");
            heightProperty = serializedObject.FindProperty("GridHeight");
            useCustomShapeProperty = serializedObject.FindProperty("UseCustomShape");
            shapeCellsProperty = serializedObject.FindProperty("ShapeCells");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "GridWidth", "GridHeight", "UseCustomShape", "ShapeCells");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Grid Footprint", EditorStyles.boldLabel);
            int oldWidth = Mathf.Clamp(widthProperty.intValue, 1, 16);
            int oldHeight = Mathf.Clamp(heightProperty.intValue, 1, 16);
            int newWidth = Mathf.Clamp(EditorGUILayout.IntField("Width", oldWidth), 1, 16);
            int newHeight = Mathf.Clamp(EditorGUILayout.IntField("Height", oldHeight), 1, 16);
            if (newWidth != oldWidth || newHeight != oldHeight)
            {
                ResizeMask(oldWidth, oldHeight, newWidth, newHeight);
                widthProperty.intValue = newWidth;
                heightProperty.intValue = newHeight;
            }

            EditorGUILayout.PropertyField(useCustomShapeProperty, new GUIContent("Use Custom Shape"));
            if (useCustomShapeProperty.boolValue)
            {
                EditorGUILayout.HelpBox("Click cells to toggle occupied space. Unpainted cells are empty and can be used by other items.", MessageType.Info);
                DrawShapePainter(newWidth, newHeight);
                int occupied = CountOccupied();
                if (occupied == 0)
                    EditorGUILayout.HelpBox("A custom item shape must contain at least one occupied cell.", MessageType.Error);
                else
                    EditorGUILayout.LabelField($"Occupied cells: {occupied} / {newWidth * newHeight}");
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Fill All")) SetAll(true);
                if (GUILayout.Button("Clear All")) SetAll(false);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Custom shape is disabled. The entire width × height rectangle is occupied.", MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawShapePainter(int width, int height)
        {
            EnsureArraySize(width * height, true);
            for (int y = 0; y < height; y++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                for (int x = 0; x < width; x++)
                {
                    SerializedProperty cell = shapeCellsProperty.GetArrayElementAtIndex(y * width + x);
                    bool occupied = cell.boolValue;
                    GUI.backgroundColor = occupied ? new Color(0.25f, 0.72f, 0.42f) : new Color(0.25f, 0.27f, 0.3f);
                    if (GUILayout.Button(occupied ? "■" : "·", GUILayout.Width(28), GUILayout.Height(28)))
                        cell.boolValue = !occupied;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ResizeMask(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            bool[] old = new bool[oldWidth * oldHeight];
            int copyCount = Mathf.Min(old.Length, shapeCellsProperty.arraySize);
            for (int i = 0; i < copyCount; i++) old[i] = shapeCellsProperty.GetArrayElementAtIndex(i).boolValue;
            shapeCellsProperty.arraySize = newWidth * newHeight;
            for (int y = 0; y < newHeight; y++)
            for (int x = 0; x < newWidth; x++)
            {
                bool value = x < oldWidth && y < oldHeight ? old[y * oldWidth + x] : true;
                shapeCellsProperty.GetArrayElementAtIndex(y * newWidth + x).boolValue = value;
            }
        }

        private void EnsureArraySize(int count, bool defaultValue)
        {
            if (shapeCellsProperty.arraySize == count) return;
            int oldSize = shapeCellsProperty.arraySize;
            shapeCellsProperty.arraySize = count;
            for (int i = oldSize; i < count; i++) shapeCellsProperty.GetArrayElementAtIndex(i).boolValue = defaultValue;
        }

        private int CountOccupied()
        {
            int count = 0;
            for (int i = 0; i < shapeCellsProperty.arraySize; i++)
                if (shapeCellsProperty.GetArrayElementAtIndex(i).boolValue) count++;
            return count;
        }

        private void SetAll(bool value)
        {
            EnsureArraySize(widthProperty.intValue * heightProperty.intValue, value);
            for (int i = 0; i < shapeCellsProperty.arraySize; i++)
                shapeCellsProperty.GetArrayElementAtIndex(i).boolValue = value;
        }
    }
}
