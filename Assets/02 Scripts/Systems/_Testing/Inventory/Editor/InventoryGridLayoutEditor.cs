using UnityEditor;
using UnityEngine;
using Systems.Inventory.Architecture;

namespace Systems.Inventory.Editor
{
    [CustomEditor(typeof(InventoryGridLayout))]
    public sealed class InventoryGridLayoutEditor : UnityEditor.Editor
    {
        private SerializedProperty widthProperty;
        private SerializedProperty heightProperty;
        private SerializedProperty usableCellsProperty;

        private void OnEnable()
        {
            widthProperty = serializedObject.FindProperty("width");
            heightProperty = serializedObject.FindProperty("height");
            usableCellsProperty = serializedObject.FindProperty("usableCells");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Inventory Grid Layout", EditorStyles.boldLabel);
            int oldWidth = Mathf.Clamp(widthProperty.intValue, 1, 24);
            int oldHeight = Mathf.Clamp(heightProperty.intValue, 1, 24);
            int newWidth = Mathf.Clamp(EditorGUILayout.IntField("Width", oldWidth), 1, 24);
            int newHeight = Mathf.Clamp(EditorGUILayout.IntField("Height", oldHeight), 1, 24);
            if (newWidth != oldWidth || newHeight != oldHeight)
            {
                ResizeMask(oldWidth, oldHeight, newWidth, newHeight);
                widthProperty.intValue = newWidth;
                heightProperty.intValue = newHeight;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Green cells are usable inventory space. Dark cells are blocked and cannot contain any part of an item. Resize the grid to create the overall layout.", MessageType.Info);
            DrawPainter(newWidth, newHeight);
            int usable = CountUsable();
            EditorGUILayout.LabelField($"Usable cells: {usable} / {newWidth * newHeight}");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable All")) SetAll(true);
            if (GUILayout.Button("Block All")) SetAll(false);
            EditorGUILayout.EndHorizontal();
            if (usable == 0)
                EditorGUILayout.HelpBox("This layout has no usable cells. Items cannot be stored until at least one cell is enabled.", MessageType.Error);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPainter(int width, int height)
        {
            EnsureArraySize(width * height, true);
            for (int y = 0; y < height; y++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                for (int x = 0; x < width; x++)
                {
                    SerializedProperty cell = usableCellsProperty.GetArrayElementAtIndex(y * width + x);
                    bool usable = cell.boolValue;
                    GUI.backgroundColor = usable ? new Color(0.25f, 0.72f, 0.42f) : new Color(0.25f, 0.27f, 0.3f);
                    if (GUILayout.Button(usable ? "■" : "·", GUILayout.Width(25), GUILayout.Height(25)))
                        cell.boolValue = !usable;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ResizeMask(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            bool[] old = new bool[oldWidth * oldHeight];
            for (int i = 0; i < old.Length && i < usableCellsProperty.arraySize; i++)
                old[i] = usableCellsProperty.GetArrayElementAtIndex(i).boolValue;
            usableCellsProperty.arraySize = newWidth * newHeight;
            for (int y = 0; y < newHeight; y++)
            for (int x = 0; x < newWidth; x++)
            {
                bool value = x < oldWidth && y < oldHeight ? old[y * oldWidth + x] : true;
                usableCellsProperty.GetArrayElementAtIndex(y * newWidth + x).boolValue = value;
            }
        }

        private void EnsureArraySize(int count, bool defaultValue)
        {
            if (usableCellsProperty.arraySize == count) return;
            int oldSize = usableCellsProperty.arraySize;
            usableCellsProperty.arraySize = count;
            for (int i = oldSize; i < count; i++)
                usableCellsProperty.GetArrayElementAtIndex(i).boolValue = defaultValue;
        }

        private int CountUsable()
        {
            int count = 0;
            for (int i = 0; i < usableCellsProperty.arraySize; i++)
                if (usableCellsProperty.GetArrayElementAtIndex(i).boolValue) count++;
            return count;
        }

        private void SetAll(bool value)
        {
            EnsureArraySize(widthProperty.intValue * heightProperty.intValue, value);
            for (int i = 0; i < usableCellsProperty.arraySize; i++)
                usableCellsProperty.GetArrayElementAtIndex(i).boolValue = value;
        }
    }
}
