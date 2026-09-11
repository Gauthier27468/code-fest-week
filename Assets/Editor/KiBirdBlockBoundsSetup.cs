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
            if (t != null && (t.name.IndexOf("WallLeft", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                              t.name.IndexOf("WallRight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                              t.name.IndexOf("WallsLeft", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                              t.name.IndexOf("WallsRight", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Transform blockRoot = t.parent;
                while (blockRoot != null && blockRoot.parent != null &&
                       (blockRoot.name == "Content" || blockRoot.name == "WallsLeft" || blockRoot.name == "WallsRight" || blockRoot.name.StartsWith("SM_")))
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
                        bounds.AutoDetectFromWalls();
                        bounds.maxHeight = BlockBounds.StandardDefaultMaxHeight;
                        bounds.minHeight = BlockBounds.StandardDefaultMinHeight;
                        EditorUtility.SetDirty(blockRoot.gameObject);
                        count++;
                    }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[KiBird] BlockBounds vérifié/configuré sur {count} nouveaux blocs dans la scène.");
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
                    bounds.AutoDetectFromWalls();
                    bounds.maxHeight = BlockBounds.StandardDefaultMaxHeight;
                    bounds.minHeight = BlockBounds.StandardDefaultMinHeight;
                    PrefabUtility.SaveAsPrefabAsset(prefab, path);
                    count++;
                }

                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KiBird] BlockBounds ajouté et configuré sur {count} préfabriqués d'environnement.");
    }
}
