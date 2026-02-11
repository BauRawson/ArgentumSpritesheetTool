using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpriteAnimationDefinition))]
public class SpriteAnimationDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SpriteAnimationDefinition def = (SpriteAnimationDefinition)target;

        DrawDefaultInspector();

        GUILayout.Space(10);

        if (GUILayout.Button("Recalculate Clip Info"))
        {
            def.Recalculate();
            EditorUtility.SetDirty(def);
        }

        if (GUILayout.Button("Auto-generate Frame List (Nth)"))
        {
            def.AutoGenerateFrames();
            EditorUtility.SetDirty(def);
        }

        if (def.clip)
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Clip Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Frame Rate", def.frameRate.ToString());
            EditorGUILayout.LabelField("Total Frames", def.totalFrames.ToString());
            EditorGUILayout.LabelField("Exported Frames", def.frameIndices.Count.ToString());
        }
    }
}
