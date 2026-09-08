using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapGenerator))]
public class MapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MapGenerator generator = (MapGenerator)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Outils & Actions Rapides", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-Assigner Préfabs Projet", GUILayout.Height(28)))
        {
            generator.AutoAssignPrefabs();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Générer Preview Éditeur", GUILayout.Height(28)))
        {
            generator.GenerateMapEditorPreview();
        }

        if (GUILayout.Button("Nettoyer Preview", GUILayout.Height(28)))
        {
            generator.ClearMapEditor();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            $"Longueur totale calculée : {generator.TotalMapLength}m ({generator.totalBlocks} blocs x {generator.blockInterval}m).\n" +
            $"Fin du circuit à Z = {generator.EndingZ}m.\n" +
            $"Brouillard : débute à {generator.fogStartBlocks * generator.blockInterval}m (3 blocs), masque tout à {generator.fogEndBlocks * generator.blockInterval}m (5 blocs).",
            MessageType.Info
        );
    }

    [MenuItem("KiBird/Ajouter MapGenerator dans la Scène Active")]
    public static void AddMapGeneratorToScene()
    {
        MapGenerator existing = Object.FindAnyObjectByType<MapGenerator>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[KiBird] MapGenerator existe déjà dans la scène.");
            return;
        }

        GameObject go = new GameObject("MapGenerator");
        Undo.RegisterCreatedObjectUndo(go, "Create MapGenerator");

        MapGenerator generator = go.AddComponent<MapGenerator>();
        generator.AutoAssignPrefabs();

        Selection.activeGameObject = go;
        Debug.Log("[KiBird] MapGenerator créé et configuré dans la scène active !");
    }
}
