using UnityEngine;
using UnityEditor;
using System.IO;

public class BulkRenameAnimationsInFBX : EditorWindow
{
    private string textToRemove = "";
    private string prefix = "";
    private string suffix = "";

    [MenuItem("Tools/Bulk Rename Animations (Remove Text)")]
    public static void ShowWindow()
    {
        GetWindow<BulkRenameAnimationsInFBX>("Rename Clips (Remove Text)");
    }

    private void OnGUI()
    {
        GUILayout.Label("Remove Specific Text from Filename", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Example: 'Great sword blocking 2.fbx'\nType 'Great sword ' into Remove Text to leave 'blocking 2'.", MessageType.Info);

        textToRemove = EditorGUILayout.TextField("Remove Text", textToRemove);

        GUILayout.Space(10);
        GUILayout.Label("Add Extras", EditorStyles.boldLabel);
        prefix = EditorGUILayout.TextField("Add Prefix", prefix);
        suffix = EditorGUILayout.TextField("Add Suffix", suffix);

        GUILayout.Space(15);
        if (GUILayout.Button("Apply to Selected FBX Assets"))
        {
            ProcessSelectedModels();
        }
    }

    private void ProcessSelectedModels()
    {
        Object[] selectedObjects = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        int modifiedCount = 0;

        foreach (Object obj in selectedObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer != null)
            {
                // 1. Get the filename without extension (e.g., "Great sword blocking 2")
                string filename = Path.GetFileNameWithoutExtension(path);
                
                // 2. Erase the unwanted phrase
                string cleanName = filename;
                if (!string.IsNullOrEmpty(textToRemove))
                {
                    // Using a case-insensitive replace so "great sword" matches "Great sword"
                    cleanName = System.Text.RegularExpressions.Regex.Replace(
                        filename, 
                        System.Text.RegularExpressions.Regex.Escape(textToRemove), 
                        "", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );
                }

                // Trim any accidental double spaces left over on the edges
                cleanName = cleanName.Trim();

                if (string.IsNullOrEmpty(cleanName))
                {
                    Debug.LogWarning($"Removing '{textToRemove}' left an empty string for file: {filename}. Skipping.");
                    continue;
                }

                // 3. Combine with Prefix and Suffix
                string finalClipBaseName = prefix + cleanName + suffix;

                // 4. Apply to the importer clips
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                bool changed = false;

                if (clips == null || clips.Length == 0)
                {
                    clips = importer.defaultClipAnimations;
                }

                if (clips != null && clips.Length > 0)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        string multiClipIndex = clips.Length > 1 ? $"_{i}" : "";
                        clips[i].name = finalClipBaseName + multiClipIndex;
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                    modifiedCount++;
                }
            }
        }

        Debug.Log($"Successfully updated animation clips across {modifiedCount} assets.");
    }
}
