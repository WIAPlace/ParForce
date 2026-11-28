using System.Collections.Generic;
using Systems.Inventory.Architecture.Networking;
using UnityEditor;
using UnityEngine;

namespace Systems.Inventory.Editor
{
    [CustomEditor(typeof(NetworkItemDefinitionCatalog))]
    public sealed class NetworkItemDefinitionCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Network Definition IDs are stable content identifiers. Zero means unassigned. Assign IDs once, commit the assets, and keep the same values across every client build. Do not regenerate IDs casually after release.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Assign Missing IDs"))
                AssignMissingIds((NetworkItemDefinitionCatalog)target);
            if (GUILayout.Button("Validate Catalog"))
                ValidateCatalog((NetworkItemDefinitionCatalog)target);
            EditorGUILayout.EndHorizontal();
        }

        private static void AssignMissingIds(NetworkItemDefinitionCatalog catalog)
        {
            SerializedObject catalogObject = new SerializedObject(catalog);
            SerializedProperty definitions = catalogObject.FindProperty("definitions");
            var used = new HashSet<ushort>();
            var visited = new HashSet<ItemDetails>();

            for (int i = 0; i < definitions.arraySize; i++)
            {
                ItemDetails details = definitions.GetArrayElementAtIndex(i).objectReferenceValue as ItemDetails;
                if (details == null || !visited.Add(details)) continue;
                if (details.NetworkDefinitionId != 0)
                    used.Add(details.NetworkDefinitionId);
            }

            int assigned = 0;
            visited.Clear();
            for (int i = 0; i < definitions.arraySize; i++)
            {
                ItemDetails details = definitions.GetArrayElementAtIndex(i).objectReferenceValue as ItemDetails;
                if (details == null || !visited.Add(details) || details.NetworkDefinitionId != 0) continue;

                ushort next = FindNextFreeId(used);
                if (next == 0)
                {
                    Debug.LogError("Network item catalog has exhausted all 65,535 non-zero definition IDs.", catalog);
                    break;
                }

                Undo.RecordObject(details, "Assign Network Item Definition ID");
                details.NetworkDefinitionId = next;
                EditorUtility.SetDirty(details);
                used.Add(next);
                assigned++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Assigned {assigned} missing network item definition ID(s) in '{catalog.name}'.", catalog);
            ValidateCatalog(catalog);
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

        private static void ValidateCatalog(NetworkItemDefinitionCatalog catalog)
        {
            if (catalog.TryBuildRegistry(out NetworkItemDefinitionRegistry registry, out List<string> errors))
            {
                Debug.Log($"Network item catalog '{catalog.name}' is valid ({registry.Count} definitions).", catalog);
                return;
            }

            Debug.LogError($"Network item catalog '{catalog.name}' has {errors.Count} problem(s):\n- " + string.Join("\n- ", errors), catalog);
        }
    }
}
