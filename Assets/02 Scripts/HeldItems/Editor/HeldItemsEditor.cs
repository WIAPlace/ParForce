using UnityEditor;
using UnityEngine;

/*
[CustomEditor(typeof(HeldItemData))]
public class PathDataEditor : Editor
{
    private void OnEnable()
    {
        // Unity calls OnSceneGUI automatically for custom Editors.
    }

    private void OnDisable()
    {
        // No manual SceneView subscription is required.
    }

    private void OnSceneGUI()
    {
        HeldItemData data = (HeldItemData)target;
        if (data == null || data.localPoints == null) return;

        Handles.color = Color.aliceBlue;

        for (int i = 0; i < data.localPoints.Length; i++)
        {
            // Draw a sphere handle
            Handles.SphereHandleCap(
                0,
                data.localPoints[i],
                Quaternion.identity,
                2f,
                EventType.Repaint
            );

            // Draw connecting lines
            if (i < data.localPoints.Length - 1)
            {
                Handles.DrawLine(data.localPoints[i], data.localPoints[i + 1]);
            }
        }
    }
}
*/