using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KiBirdBlockBoundsSetup
{
    private const string GameScenePath = "Assets/Scenes/Blocks.unity";

    [MenuItem("KiBird/Setup Block Bounds in Active Scene")]
    public static void SetupSceneBounds()
    {
        int count = 0;
        var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        var processedRoots = new System.Collections.Generic.HashSet<Transform>();

        foreach (var t in allTransforms)
        {
            if (t != null && (t.name == "WallLeft" || t.name == "WallRight"))
            {
                Transform blockRoot = t.parent;
                if (blockRoot != null && blockRoot.name == "Content")
                {
                    blockRoot = blockRoot.parent;
                }

                if (blockRoot != null && !processedRoots.Contains(blockRoot))
                {
                    processedRoots.Add(blockRoot);

                    var bounds = blockRoot.GetComponent<BlockBounds>();
                    if (bounds == null)
                    {
                        bounds = Undo.AddComponent<BlockBounds>(blockRoot.gameObject);
                    }

                    bounds.AutoDetectFromWalls();
                    bounds.maxHeight = 9.5f;
                    bounds.minHeight = 1.5f;
                    EditorUtility.SetDirty(blockRoot.gameObject);
                    count++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[KiBird] BlockBounds configuré sur {count} blocs dans la scène.");
    }

    [MenuItem("KiBird/Add BlockBounds to Environment Prefabs")]
    public static void SetupPrefabsBounds()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Environment" });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = PrefabUtility.LoadPrefabContents(path);

            if (prefab != null)
            {
                var bounds = prefab.GetComponent<BlockBounds>();
                if (bounds == null)
                {
                    bounds = prefab.AddComponent<BlockBounds>();
                }

                bounds.AutoDetectFromWalls();
                bounds.maxHeight = 9.5f;
                bounds.minHeight = 1.5f;

                PrefabUtility.SaveAsPrefabAsset(prefab, path);
                PrefabUtility.UnloadPrefabContents(prefab);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KiBird] BlockBounds ajouté et configuré sur {count} préfabriqués d'environnement.");
    }
}
