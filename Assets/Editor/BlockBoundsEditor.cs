using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BlockBounds))]
[CanEditMultipleObjects]
public class BlockBoundsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        BlockBounds bounds = (BlockBounds)target;

        EditorGUILayout.Space(4);

        float width = bounds.CurrentWidth;
        float standardWidth = BlockBounds.StandardDefaultMaxX - BlockBounds.StandardDefaultMinX;
        bool isWidened = width > standardWidth * 1.15f;

        if (isWidened)
        {
            EditorGUILayout.HelpBox($"Zone élargie : {width:F1}m (Standard: {standardWidth:F1}m). Idéal pour les zones d'obstacles larges (ex: Fortnite BattleBus).", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"Zone standard : {width:F1}m (Centre X = {bounds.CenterX:F1}m).", MessageType.None);
        }

        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Actions Rapides de Largeur (Clamping)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Élargir (+50%)", GUILayout.Height(26)))
        {
            Undo.RecordObjects(targets, "Expand Width 50%");
            foreach (var t in targets)
            {
                ((BlockBounds)t).ExpandWidth(1.5f);
                EditorUtility.SetDirty(t);
            }
        }

        if (GUILayout.Button("Doubler (+100%)", GUILayout.Height(26)))
        {
            Undo.RecordObjects(targets, "Expand Width 100%");
            foreach (var t in targets)
            {
                ((BlockBounds)t).ExpandWidth(2.0f);
                EditorUtility.SetDirty(t);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Détecter Murs", GUILayout.Height(24)))
        {
            Undo.RecordObjects(targets, "Auto Detect Walls");
            foreach (var t in targets)
            {
                ((BlockBounds)t).AutoDetectFromWalls();
                EditorUtility.SetDirty(t);
            }
        }

        if (GUILayout.Button("Réinitialiser Standard", GUILayout.Height(24)))
        {
            Undo.RecordObjects(targets, "Reset To Default");
            foreach (var t in targets)
            {
                ((BlockBounds)t).ResetToDefault();
                EditorUtility.SetDirty(t);
            }
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }
}
