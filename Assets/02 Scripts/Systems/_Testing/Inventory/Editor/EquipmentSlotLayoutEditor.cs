using System.Collections.Generic;
using Systems.Inventory.Architecture;
using Systems.Inventory.Architecture.Networking;
using UnityEditor;
using UnityEngine;

namespace Systems.Inventory.Editor
{
    [CustomEditor(typeof(EquipmentSlotLayout))]
    public sealed class EquipmentSlotLayoutEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Network Slot IDs are stable compact identifiers. Zero means unassigned. Assign IDs once and keep the same values across all clients. Reordering slots does not change these IDs.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Assign Missing Network IDs"))
                AssignMissingIds((EquipmentSlotLayout)target);
            if (GUILayout.Button("Validate Network IDs"))
                ValidateLayout((EquipmentSlotLayout)target);
            EditorGUILayout.EndHorizontal();
        }

        private static void AssignMissingIds(EquipmentSlotLayout layout)
        {
            SerializedObject layoutObject = new SerializedObject(layout);
            SerializedProperty slots = layoutObject.FindProperty("slots");
            var used = new HashSet<ushort>();

            for (int i = 0; i < slots.arraySize; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                SerializedProperty networkId = slot.FindPropertyRelative("networkSlotId");
                if (networkId != null && networkId.intValue > 0)
                    used.Add((ushort)networkId.intValue);
            }

            int assigned = 0;
            for (int i = 0; i < slots.arraySize; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                SerializedProperty networkId = slot.FindPropertyRelative("networkSlotId");
                if (networkId == null || networkId.intValue != 0)
                    continue;

                ushort next = FindNextFreeId(used);
                if (next == 0)
                {
                    Debug.LogError("Equipment layout has exhausted all 65,535 non-zero network slot IDs.", layout);
                    break;
                }

                networkId.intValue = next;
                used.Add(next);
                assigned++;
            }

            layoutObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            Debug.Log($"Assigned {assigned} missing network slot ID(s) in '{layout.name}'.", layout);
            ValidateLayout(layout);
        }

        private static ushort FindNextFreeId(HashSet<ushort> used)
        {
            for (int value = 1; value <= ushort.MaxValue; value++)
            {
                ushort candidate = (ushort)value;
                if (!used.Contains(candidate)) return candidate;
            }
            return 0;
        }

        private static void ValidateLayout(EquipmentSlotLayout layout)
        {
            if (NetworkEquipmentSlotRegistry.TryCreate(layout.Slots, out NetworkEquipmentSlotRegistry registry, out List<string> errors))
            {
                Debug.Log($"Equipment slot layout '{layout.name}' is valid ({registry.Count} slots).", layout);
                return;
            }

            Debug.LogError($"Equipment slot layout '{layout.name}' has {errors.Count} problem(s):\n- " + string.Join("\n- ", errors), layout);
        }
    }
}
