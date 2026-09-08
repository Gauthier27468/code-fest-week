using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Générateur procédural de circuit (KiBird).
/// - Aligne N blocs sur l'axe Z espacés de 48m.
/// - Séquence garantie : Intro (0), Tuto 1 (1), Tuto 2 (2), Blocs aléatoires (3 .. N-2), Ending Block (N-1).
/// - Détruit automatiquement les blocs dépassés derrière l'oiseau pour libérer la mémoire.
/// - Configure le brouillard (fog) et le farClipPlane de la caméra pour n'afficher que 3 à 5 blocs devant.
/// </summary>
public class MapGenerator : MonoBehaviour
{
    public static MapGenerator Instance { get; private set; }

    [Header("Configuration des Blocs (Nombre Total N)")]
    [Tooltip("Nombre total n de blocs à générer pour la course (inclut Intro, Tuto 1, Tuto 2, blocs aléatoires et Ending). Minimum 4.")]
    [Range(4, 100)]
    public int totalBlocks = 12;

    [Tooltip("Espacement régulier sur l'axe Z entre l'origine de chaque bloc (calibré à 48m).")]
    public float blockInterval = 48f;

    [Tooltip("Position Z du tout premier bloc (Intro).")]
    public float startZ = 0f;

    [Header("Préfabs de la Séquence Fixe")]
    [Tooltip("1er bloc obligatoire : Intro")]
    public GameObject introPrefab;

    [Tooltip("2ème bloc obligatoire : Tutoriel 1")]
    public GameObject tuto1Prefab;

    [Tooltip("3ème bloc obligatoire : Tutoriel 2")]
    public GameObject tuto2Prefab;

    [Tooltip("Dernier bloc obligatoire (N-1) : Ending Block")]
    public GameObject endingPrefab;

    [Header("Pool de Blocs Aléatoires")]
    [Tooltip("Liste des préfabs de blocs d'obstacles/gameplay tirés au sort entre les tutos et la fin.")]
    public List<GameObject> randomBlockPrefabs = new List<GameObject>();

    [Tooltip("Évite de piocher deux fois d'affilée le même bloc aléatoire.")]
    public bool avoidConsecutiveDuplicates = true;

    [Header("Streaming & Nettoyage (Performances)")]
    [Tooltip("Si activé, instancie les blocs progressivement à l'approche de l'oiseau. Si désactivé, instancie tous les N blocs au Start().")]
    public bool streamBlocksAhead = true;

    [Tooltip("Nombre de blocs à garder instanciés d'avance devant l'oiseau (3 à 5 recommandé pour correspondre au brouillard).")]
    [Range(2, 10)]
    public int spawnAheadBlocks = 5;

    [Tooltip("Détruit automatiquement un bloc une fois que l'oiseau l'a dépassé pour alléger la scène.")]
    public bool destroyPassedBlocks = true;

    [Tooltip("Distance derrière l'oiseau à partir de laquelle un bloc dépassé est détruit (en mètres).")]
    public float destroyDistanceBehind = 24f;

    [Header("Effet de Brouillard (Fog & Caméra)")]
    [Tooltip("Active et configure le brouillard Unity pour masquer les blocs au-delà de 3 à 5 blocs.")]
    public bool enableFog = true;

    [Tooltip("Distance (en nombre de blocs) où le brouillard commence à apparaître (ex: 3 blocs = 144m).")]
    public float fogStartBlocks = 3f;

    [Tooltip("Distance (en nombre de blocs) où le brouillard devient totalement opaque (ex: 5 blocs = 240m).")]
    public float fogEndBlocks = 5f;

    [Tooltip("Synchronise la couleur du brouillard avec le fond de la caméra principale (recommandé pour un fondu invisible).")]
    public bool syncFogWithCameraBackground = true;

    [Tooltip("Couleur personnalisée du brouillard si la synchronisation caméra est désactivée.")]
    public Color customFogColor = new Color(0.192f, 0.302f, 0.475f, 1f);

    [Tooltip("Ajuste le farClipPlane de la caméra principale à la fin du brouillard pour ne pas calculer les polygones invisibles.")]
    public bool adjustCameraFarClip = true;

    [Header("Hiérarchie & Références")]
    [Tooltip("Transform parent sous lequel instancier les blocs générés. Si vide, utilise ce GameObject.")]
    public Transform blocksParent;

    [Tooltip("Transform de l'oiseau. Trouvé automatiquement via MoveBird ou tag Player si laissé vide.")]
    public Transform birdTransform;

    [Tooltip("Nettoie les éventuels blocs statiques déjà présents dans la scène au démarrage pour éviter tout doublon.")]
    public bool cleanSceneBlocksAtStart = true;

    [System.Serializable]
    public class BlockSlot
    {
        public int index;
        public float zPosition;
        public GameObject prefab;
        public GameObject instance;
        public bool isSpawned => instance != null;
        public bool isDestroyed;
    }

    private BlockSlot[] slots;
    private GameObject lastRandomPicked;

    public float TotalMapLength => Mathf.Max(4, totalBlocks) * blockInterval;
    public float EndingZ => startZ + (Mathf.Max(4, totalBlocks) - 1) * blockInterval;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (blocksParent == null)
        {
            blocksParent = transform;
        }
    }

    private void Start()
    {
        FindBird();

        if (cleanSceneBlocksAtStart)
        {
            CleanExistingSceneBlocks();
        }

        SetupFog();
        BuildPlan();

        if (!streamBlocksAhead)
        {
            // Instanciation de tous les blocs d'un coup au démarrage
            for (int i = 0; i < slots.Length; i++)
            {
                SpawnBlock(i);
            }
        }
        else
        {
            // Instanciation initiale de la fenêtre de départ
            UpdateStreaming(birdTransform != null ? birdTransform.position.z : startZ);
        }
    }

    private void Update()
    {
        if (birdTransform == null)
        {
            FindBird();
            if (birdTransform == null) return;
        }

        float birdZ = birdTransform.position.z;

        if (streamBlocksAhead)
        {
            UpdateStreaming(birdZ);
        }

        if (destroyPassedBlocks)
        {
            UpdateDestruction(birdZ);
        }
    }

    /// <summary>
    /// Construit le plan complet des N blocs selon les règles :
    /// 0 : Intro
    /// 1 : Tuto 1
    /// 2 : Tuto 2
    /// 3 .. N-2 : Aléatoires
    /// N-1 : Ending Block
    /// </summary>
    public void BuildPlan()
    {
        int count = Mathf.Max(4, totalBlocks);
        slots = new BlockSlot[count];

        lastRandomPicked = null;

        for (int i = 0; i < count; i++)
        {
            GameObject chosenPrefab;

            if (i == 0)
            {
                chosenPrefab = introPrefab;
            }
            else if (i == 1)
            {
                chosenPrefab = tuto1Prefab;
            }
            else if (i == 2)
            {
                chosenPrefab = tuto2Prefab;
            }
            else if (i == count - 1)
            {
                chosenPrefab = endingPrefab;
            }
            else
            {
                chosenPrefab = PickRandomBlock();
                lastRandomPicked = chosenPrefab;
            }

            slots[i] = new BlockSlot
            {
                index = i,
                zPosition = startZ + i * blockInterval,
                prefab = chosenPrefab,
                instance = null,
                isDestroyed = false
            };
        }
    }

    private GameObject PickRandomBlock()
    {
        if (randomBlockPrefabs == null || randomBlockPrefabs.Count == 0)
        {
            return null;
        }

        if (randomBlockPrefabs.Count == 1)
        {
            return randomBlockPrefabs[0];
        }

        GameObject picked = null;
        int attempts = 10;
        while (attempts-- > 0)
        {
            picked = randomBlockPrefabs[Random.Range(0, randomBlockPrefabs.Count)];
            if (!avoidConsecutiveDuplicates || picked != lastRandomPicked)
            {
                break;
            }
        }

        return picked != null ? picked : randomBlockPrefabs[0];
    }

    private void UpdateStreaming(float birdZ)
    {
        if (slots == null) return;

        float maxSpawnZ = birdZ + (spawnAheadBlocks * blockInterval);

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.isDestroyed || slot.isSpawned) continue;

            if (slot.zPosition <= maxSpawnZ)
            {
                SpawnBlock(i);
            }
            else
            {
                // Comme les blocs sont ordonnés en Z croissant, on peut s'arrêter dès qu'un bloc dépasse la distance
                break;
            }
        }
    }

    private void SpawnBlock(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        var slot = slots[index];
        if (slot.isSpawned || slot.isDestroyed || slot.prefab == null) return;

        Transform parent = blocksParent != null ? blocksParent : transform;
        Vector3 pos = new Vector3(0f, 0f, slot.zPosition);

        GameObject instance = Instantiate(slot.prefab, pos, Quaternion.identity, parent);
        instance.name = $"[{slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
        slot.instance = instance;
    }

    private void UpdateDestruction(float birdZ)
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.isSpawned || slot.isDestroyed) continue;

            // La sortie du bloc est située à zPosition + blockInterval
            float blockExitZ = slot.zPosition + blockInterval;

            if (birdZ > blockExitZ + destroyDistanceBehind)
            {
                if (slot.instance != null)
                {
                    Destroy(slot.instance);
                }
                slot.instance = null;
                slot.isDestroyed = true;
            }
        }
    }

    /// <summary>
    /// Configure le brouillard (Linear Fog) et le farClipPlane de la caméra.
    /// </summary>
    public void SetupFog()
    {
        if (!enableFog) return;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;

        float startDist = Mathf.Max(0f, fogStartBlocks * blockInterval);
        float endDist = Mathf.Max(startDist + 10f, fogEndBlocks * blockInterval);

        RenderSettings.fogStartDistance = startDist;
        RenderSettings.fogEndDistance = endDist;

        Camera cam = Camera.main;
        Color fogCol = customFogColor;

        if (cam != null)
        {
            if (syncFogWithCameraBackground)
            {
                fogCol = cam.backgroundColor;
            }

            if (adjustCameraFarClip)
            {
                cam.farClipPlane = endDist + 20f;
            }
        }

        RenderSettings.fogColor = fogCol;
    }

    private void FindBird()
    {
        if (birdTransform != null) return;

        MoveBird birdScript = Object.FindAnyObjectByType<MoveBird>();
        if (birdScript != null)
        {
            birdTransform = birdScript.transform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            birdTransform = playerObj.transform;
            return;
        }

        if (Camera.main != null)
        {
            birdTransform = Camera.main.transform;
        }
    }

    private void CleanExistingSceneBlocks()
    {
        // Supprime les blocs statiques de la scène qui ne font pas partie de ce générateur
        var existingBounds = Object.FindObjectsByType<BlockBounds>(FindObjectsSortMode.None);
        foreach (var b in existingBounds)
        {
            if (b != null && b.transform != transform && !b.transform.IsChildOf(transform) && (blocksParent == null || (b.transform != blocksParent && !b.transform.IsChildOf(blocksParent))))
            {
                Transform root = b.transform;
                Destroy(root.gameObject);
            }
        }
    }

    private void OnValidate()
    {
        totalBlocks = Mathf.Max(4, totalBlocks);
        blockInterval = Mathf.Max(1f, blockInterval);
        if (enableFog)
        {
            SetupFog();
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-Assigner Préfabs depuis le Projet")]
    public void AutoAssignPrefabs()
    {
        string envPath = "Assets/Prefabs/Environment/";

        introPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Intro.prefab");
        tuto1Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Turoriel1.prefab");
        tuto2Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Turoriel2.prefab");
        endingPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "EndingBlock.prefab");

        string[] randomNames = new string[]
        {
            "ArchesBlock.prefab",
            "FallenRockBlock.prefab",
            "HouseBlock.prefab",
            "PlaneBlock.prefab",
            "SimpleBlock.prefab",
            "SplitBlock.prefab",
            "UnexpectedFall.prefab"
        };

        randomBlockPrefabs.Clear();
        foreach (string name in randomNames)
        {
            GameObject go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + name);
            if (go != null)
            {
                randomBlockPrefabs.Add(go);
            }
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[MapGenerator] Préfabs assignés avec succès ! (Intro, Tuto1, Tuto2, Ending + {randomBlockPrefabs.Count} blocs aléatoires)");
    }

    [ContextMenu("Générer la Map (Preview Éditeur)")]
    public void GenerateMapEditorPreview()
    {
        ClearMapEditor();
        BuildPlan();

        Transform parent = blocksParent != null ? blocksParent : transform;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot != null && slot.prefab != null)
            {
                GameObject instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(slot.prefab, parent);
                instance.transform.position = new Vector3(0f, 0f, slot.zPosition);
                instance.name = $"[PREVIEW {slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
                slot.instance = instance;
            }
        }

        Debug.Log($"[MapGenerator] Preview de la map générée dans l'éditeur ({slots.Length} blocs).");
    }

    [ContextMenu("Nettoyer la Map (Éditeur)")]
    public void ClearMapEditor()
    {
        Transform parent = blocksParent != null ? blocksParent : transform;
        List<GameObject> toDestroy = new List<GameObject>();

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            toDestroy.Add(child.gameObject);
        }

        foreach (var go in toDestroy)
        {
            DestroyImmediate(go);
        }

        slots = null;
        Debug.Log("[MapGenerator] Blocs nettoyés.");
    }

    private void Reset()
    {
        AutoAssignPrefabs();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        int count = Mathf.Max(4, totalBlocks);
        Gizmos.color = Color.green;

        for (int i = 0; i < count; i++)
        {
            float z = startZ + i * blockInterval;
            Vector3 center = new Vector3(3.9f, 5.5f, z + blockInterval * 0.5f);
            Vector3 size = new Vector3(9.8f, 8f, blockInterval);
            Gizmos.DrawWireCube(center, size);
        }

        // Ligne de fog
        if (enableFog)
        {
            float birdZ = birdTransform != null ? birdTransform.position.z : startZ;
            float fogStart = birdZ + fogStartBlocks * blockInterval;
            float fogEnd = birdZ + fogEndBlocks * blockInterval;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(-5f, 5f, fogStart), new Vector3(15f, 5f, fogStart));

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(-5f, 5f, fogEnd), new Vector3(15f, 5f, fogEnd));
        }
    }
}
