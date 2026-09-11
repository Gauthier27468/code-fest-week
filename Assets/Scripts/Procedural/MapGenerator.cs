using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Générateur procédural du circuit : N blocs alignés sur l'axe Z, espacés de blockInterval.
/// Séquence garantie : Intro (0), Tuto 1 (1), Tuto 2 (2), blocs aléatoires (3..N-2), Ending (N-1).
/// Les blocs sont instanciés à l'approche de l'oiseau et détruits une fois dépassés.
/// </summary>
public class MapGenerator : MonoBehaviour
{
    public static MapGenerator Instance { get; private set; }

    [Header("Configuration des Blocs (Nombre Total N)")]
    [Tooltip("Nombre total de blocs de la course, Intro / Tutos / Ending compris. Minimum 4.")]
    [Range(4, 100)]
    public int totalBlocks = 12;

    [Tooltip("Espacement régulier sur l'axe Z entre l'origine de chaque bloc.")]
    public float blockInterval = 48f;

    [Tooltip("Position Z du tout premier bloc (Intro).")]
    public float startZ = 0f;

    [Header("Préfabs de la Séquence Fixe")]
    public GameObject introPrefab;
    public GameObject tuto1Prefab;
    public GameObject tuto2Prefab;
    public GameObject endingPrefab;

    [Header("Pool de Blocs Aléatoires")]
    [Tooltip("Blocs d'obstacles tirés au sort entre les tutos et la fin.")]
    public List<GameObject> randomBlockPrefabs = new List<GameObject>();

    [Tooltip("Tirage sans remise : un même bloc ne réapparaît qu'une fois le pool épuisé.")]
    public bool noDuplicates = true;

    [Header("Streaming & Nettoyage")]
    [Tooltip("Nombre de blocs gardés instanciés d'avance devant l'oiseau.")]
    [Range(2, 10)]
    public int spawnAheadBlocks = 5;

    [Tooltip("Détruit un bloc une fois que l'oiseau l'a dépassé.")]
    public bool destroyPassedBlocks = true;

    [Tooltip("Distance derrière l'oiseau à partir de laquelle un bloc dépassé est détruit (m).")]
    public float destroyDistanceBehind = 24f;

    [Header("Brouillard & Caméra")]
    [Tooltip("Masque les blocs au-delà de la distance de rendu utile.")]
    public bool enableFog = true;

    [Tooltip("Distance, en nombre de blocs, où le brouillard commence.")]
    public float fogStartBlocks = 3f;

    [Tooltip("Distance, en nombre de blocs, où le brouillard devient opaque.")]
    public float fogEndBlocks = 5f;

    [Tooltip("Aligne le farClipPlane de la caméra sur la fin du brouillard.")]
    public bool adjustCameraFarClip = true;

    [Header("Optimisation des Colliders (Fenêtre Active n et n+1)")]
    [Tooltip("N'active les colliders solides que du bloc courant et des suivants. Les triggers (anneaux, nid, events) restent toujours actifs.")]
    public bool preloadOnlyCurrentAndNextColliders = true;

    [Tooltip("Nombre de blocs d'avance dont les colliders solides sont actifs (1 = bloc n + bloc n+1).")]
    [Range(1, 5)]
    public int aheadBlocksColliderCount = 1;

    [Tooltip("Conserve les colliders du bloc précédent (n-1) actifs pour éviter tout trou physique à la frontière.")]
    public bool keepPreviousBlockColliders = false;

    [Header("Hiérarchie & Références")]
    [Tooltip("Parent des blocs générés. Si vide, ce GameObject.")]
    public Transform blocksParent;

    [Tooltip("Transform de l'oiseau. Trouvé via MoveBird si laissé vide.")]
    public Transform birdTransform;

    /// <summary>Emplacement d'un bloc du circuit : prévu, instancié, puis détruit.</summary>
    private class BlockSlot
    {
        public int index;
        public float zPosition;
        public GameObject prefab;
        public GameObject instance;
        public bool isDestroyed;
        public Collider[] solidColliders;
        public bool collidersActive = true;

        public bool IsSpawned => instance != null;

        public void SetSolidCollidersActive(bool active, bool force = false)
        {
            if ((!force && collidersActive == active) || solidColliders == null) return;
            collidersActive = active;
            foreach (Collider col in solidColliders)
            {
                if (col != null) col.enabled = active;
            }
        }
    }

    private BlockSlot[] slots;
    private GameObject lastRandomPicked;
    private int lastColliderUpdateBlockIndex = -1;

    private int BlockCount => Mathf.Max(4, totalBlocks);
    public float TotalMapLength => BlockCount * blockInterval;
    public float EndingZ => startZ + (BlockCount - 1) * blockInterval;

    private Transform Parent => blocksParent != null ? blocksParent : transform;
    private float BirdZ => birdTransform != null ? birdTransform.position.z : startZ;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (blocksParent == null) blocksParent = transform;
    }

    private void Start()
    {
        FindBird();
        CleanExistingSceneBlocks();
        SetupFog();
        BuildPlan();

        float birdZ = BirdZ;
        UpdateStreaming(birdZ);
        if (preloadOnlyCurrentAndNextColliders)
        {
            UpdateActiveColliders(BlockIndexAt(birdZ), force: true);
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
        UpdateStreaming(birdZ);

        if (preloadOnlyCurrentAndNextColliders) UpdateActiveColliders(BlockIndexAt(birdZ));
        if (destroyPassedBlocks) UpdateDestruction(birdZ);
    }

    private void OnValidate()
    {
        totalBlocks = Mathf.Max(4, totalBlocks);
        blockInterval = Mathf.Max(1f, blockInterval);
        SetupFog();
    }

    // ------------------------------------------------------------------ plan du circuit

    /// <summary>Construit le plan des N blocs : Intro, Tuto 1, Tuto 2, aléatoires, Ending.</summary>
    private void BuildPlan()
    {
        int count = BlockCount;
        slots = new BlockSlot[count];
        lastRandomPicked = null;
        var pool = new List<GameObject>(); // blocs restant à tirer (mode noDuplicates)

        for (int i = 0; i < count; i++)
        {
            GameObject prefab;
            if (i == 0) prefab = introPrefab;
            else if (i == 1) prefab = tuto1Prefab;
            else if (i == 2) prefab = tuto2Prefab;
            else if (i == count - 1) prefab = endingPrefab;
            else prefab = PickRandomBlock(pool);

            slots[i] = new BlockSlot
            {
                index = i,
                zPosition = startZ + i * blockInterval,
                prefab = prefab
            };
        }
    }

    /// <summary>
    /// Tire un bloc au sort en évitant de répéter le précédent. En mode noDuplicates, le tirage
    /// se fait sans remise dans pool, rechargé une fois épuisé.
    /// </summary>
    private GameObject PickRandomBlock(List<GameObject> pool)
    {
        if (noDuplicates && pool.Count == 0)
        {
            foreach (GameObject prefab in randomBlockPrefabs)
            {
                if (prefab != null && !pool.Contains(prefab)) pool.Add(prefab);
            }
        }

        List<GameObject> source = noDuplicates ? pool : randomBlockPrefabs;
        if (source.Count == 0) return null;

        int index = Random.Range(0, source.Count);
        // Quelques retirages suffisent à éviter le bloc précédent quand il y a le choix.
        for (int attempt = 0; attempt < 10 && source.Count > 1 && source[index] == lastRandomPicked; attempt++)
        {
            index = Random.Range(0, source.Count);
        }

        GameObject picked = source[index];
        if (noDuplicates) pool.RemoveAt(index);
        lastRandomPicked = picked;
        return picked;
    }

    // ------------------------------------------------------------------ streaming des blocs

    /// <summary>Instancie les blocs qui entrent dans la fenêtre d'avance de l'oiseau.</summary>
    private void UpdateStreaming(float birdZ)
    {
        if (slots == null) return;

        float maxSpawnZ = birdZ + spawnAheadBlocks * blockInterval;
        foreach (BlockSlot slot in slots)
        {
            // Blocs ordonnés en Z croissant : le premier trop loin arrête le parcours.
            if (slot.zPosition > maxSpawnZ) break;
            if (!slot.isDestroyed && !slot.IsSpawned) SpawnBlock(slot);
        }
    }

    private void SpawnBlock(BlockSlot slot)
    {
        if (slot.prefab == null) return;

        slot.instance = Instantiate(slot.prefab, new Vector3(0f, 0f, slot.zPosition), Quaternion.identity, Parent);
        slot.instance.name = $"[{slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
        CacheSolidColliders(slot);
    }

    /// <summary>Détruit les blocs que l'oiseau a dépassés de plus de destroyDistanceBehind.</summary>
    private void UpdateDestruction(float birdZ)
    {
        if (slots == null) return;

        foreach (BlockSlot slot in slots)
        {
            if (!slot.IsSpawned || slot.isDestroyed) continue;

            float blockExitZ = slot.zPosition + blockInterval;
            if (birdZ > blockExitZ + destroyDistanceBehind)
            {
                Destroy(slot.instance);
                slot.instance = null;
                slot.solidColliders = null;
                slot.isDestroyed = true;
            }
        }
    }

    // ------------------------------------------------------------------ fenêtre de colliders actifs

    /// <summary>
    /// Met en cache les colliders solides (!isTrigger) du bloc et les règle selon la fenêtre
    /// active. Les triggers (anneaux, nid, events) ne sont jamais touchés.
    /// </summary>
    private void CacheSolidColliders(BlockSlot slot)
    {
        var solids = new List<Collider>();
        foreach (Collider col in slot.instance.GetComponentsInChildren<Collider>(true))
        {
            if (!col.isTrigger) solids.Add(col);
        }
        slot.solidColliders = solids.ToArray();

        if (preloadOnlyCurrentAndNextColliders)
        {
            slot.SetSolidCollidersActive(IsInActiveColliderWindow(slot.index, BlockIndexAt(BirdZ)), force: true);
        }
    }

    /// <summary>Index du bloc situé à la position Z donnée.</summary>
    private int BlockIndexAt(float z)
    {
        int index = Mathf.FloorToInt((z - startZ) / blockInterval);
        return Mathf.Clamp(index, 0, BlockCount - 1);
    }

    /// <summary>Le bloc fait-il partie de la fenêtre active (n, n+1... et éventuellement n-1) ?</summary>
    private bool IsInActiveColliderWindow(int blockIndex, int currentBlock)
    {
        int minActive = keepPreviousBlockColliders ? currentBlock - 1 : currentBlock;
        int maxActive = currentBlock + aheadBlocksColliderCount;
        return blockIndex >= minActive && blockIndex <= maxActive;
    }

    /// <summary>Réajuste les colliders à chaque changement de bloc. Zéro allocation.</summary>
    private void UpdateActiveColliders(int currentBlock, bool force = false)
    {
        if (slots == null || (!force && currentBlock == lastColliderUpdateBlockIndex)) return;
        lastColliderUpdateBlockIndex = currentBlock;

        foreach (BlockSlot slot in slots)
        {
            if (slot.IsSpawned && !slot.isDestroyed)
            {
                slot.SetSolidCollidersActive(IsInActiveColliderWindow(slot.index, currentBlock));
            }
        }
    }

    // ------------------------------------------------------------------ mise en place

    /// <summary>Brouillard linéaire, et farClipPlane de la caméra calé sur sa fin.</summary>
    private void SetupFog()
    {
        if (!enableFog) return;

        float startDist = Mathf.Max(0f, fogStartBlocks * blockInterval);
        float endDist = Mathf.Max(startDist + 10f, fogEndBlocks * blockInterval);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = startDist;
        RenderSettings.fogEndDistance = endDist;

        Camera cam = Camera.main;
        if (cam != null)
        {
            RenderSettings.fogColor = cam.backgroundColor;
            if (adjustCameraFarClip) cam.farClipPlane = endDist + 20f;
        }
    }

    private void FindBird()
    {
        if (birdTransform != null) return;

        MoveBird bird = FindAnyObjectByType<MoveBird>();
        if (bird != null) birdTransform = bird.transform;
    }

    /// <summary>Supprime les blocs statiques laissés dans la scène, hors de ce générateur.</summary>
    private void CleanExistingSceneBlocks()
    {
        foreach (BlockBounds block in FindObjectsByType<BlockBounds>())
        {
            if (block.transform.IsChildOf(transform) || block.transform.IsChildOf(Parent)) continue;
            Destroy(block.gameObject);
        }
    }

    // ------------------------------------------------------------------ outils éditeur

#if UNITY_EDITOR
    [ContextMenu("Auto-Assigner Préfabs depuis le Projet")]
    public void AutoAssignPrefabs()
    {
        const string envPath = "Assets/Prefabs/Environment/";

        introPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Intro.prefab");
        tuto1Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Turoriel1.prefab");
        tuto2Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "Turoriel2.prefab");
        endingPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + "EndingBlock.prefab");

        string[] randomNames =
        {
            "ArchesBlock.prefab",
            "BattleBusBlock.prefab",
            "FallenRockBlock.prefab",
            "HouseBlock.prefab",
            "PlaneBlock.prefab",
            "SimpleBlock.prefab",
            "SplitBlock.prefab",
            "UnexpectedFall.prefab"
        };

        randomBlockPrefabs.Clear();
        foreach (string prefabName in randomNames)
        {
            GameObject go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + prefabName);
            if (go != null) randomBlockPrefabs.Add(go);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[MapGenerator] Préfabs assignés ({randomBlockPrefabs.Count} blocs aléatoires).");
    }

    [ContextMenu("Générer la Map (Preview Éditeur)")]
    public void GenerateMapEditorPreview()
    {
        ClearMapEditor();
        BuildPlan();

        foreach (BlockSlot slot in slots)
        {
            if (slot.prefab == null) continue;

            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(slot.prefab, Parent);
            instance.transform.position = new Vector3(0f, 0f, slot.zPosition);
            instance.name = $"[PREVIEW {slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
        }

        Debug.Log($"[MapGenerator] Preview générée ({slots.Length} blocs).");
    }

    [ContextMenu("Nettoyer la Map (Éditeur)")]
    public void ClearMapEditor()
    {
        Transform parent = Parent;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(parent.GetChild(i).gameObject);
        }

        slots = null;
        Debug.Log("[MapGenerator] Blocs nettoyés.");
    }

    private void Reset()
    {
        AutoAssignPrefabs();
    }
#endif

    /// <summary>Blocs (vert = colliders actifs, gris = inactifs) et bornes du brouillard.</summary>
    private void OnDrawGizmosSelected()
    {
        int currentBlock = BlockIndexAt(BirdZ);

        for (int i = 0; i < BlockCount; i++)
        {
            float z = startZ + i * blockInterval;
            if (Application.isPlaying && preloadOnlyCurrentAndNextColliders)
            {
                Gizmos.color = IsInActiveColliderWindow(i, currentBlock)
                    ? new Color(0f, 1f, 0.2f, 0.85f)
                    : new Color(0.4f, 0.4f, 0.4f, 0.2f);
            }
            else
            {
                Gizmos.color = Color.green;
            }

            Gizmos.DrawWireCube(new Vector3(3.9f, 5.5f, z + blockInterval * 0.5f), new Vector3(9.8f, 8f, blockInterval));
        }

        if (!enableFog) return;

        float fogStart = BirdZ + fogStartBlocks * blockInterval;
        float fogEnd = BirdZ + fogEndBlocks * blockInterval;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(-5f, 5f, fogStart), new Vector3(15f, 5f, fogStart));
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(-5f, 5f, fogEnd), new Vector3(15f, 5f, fogEnd));
    }
}
