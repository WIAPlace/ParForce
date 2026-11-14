using UnityEditor;
using UnityEngine;
/*
[CustomEditor(typeof(HeldItemData))]
public class HeldItemsEditor : Editor
{
    private void OnEnable()
    {
        SceneView.duringSceneGui += DrawSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawSceneGUI;
    }

    private void DrawSceneGUI(SceneView sceneView)
    {
        HeldItemData data = (HeldItemData)target;
        if (data == null || data.localPoints == null) return;

        Handles.color = Color.cyan;
        foreach (Vector3 point in data.localPoints)
        {
            Handles.SphereHandleCap(0, point, Quaternion.identity, 0.5f, EventType.Repaint);
        }
    }
}
*/