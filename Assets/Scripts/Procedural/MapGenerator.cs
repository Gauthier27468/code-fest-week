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

    [Header("Hiérarchie & Références")]
    [Tooltip("Parent des blocs générés. Si vide, ce GameObject.")]
    public Transform blocksParent;

    [Tooltip("Transform de l'oiseau. Trouvé via MoveBird ou le tag Player si laissé vide.")]
    public Transform birdTransform;

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
        CleanExistingSceneBlocks();
        SetupFog();
        BuildPlan();
        UpdateStreaming(birdTransform != null ? birdTransform.position.z : startZ);
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

        if (destroyPassedBlocks)
        {
            UpdateDestruction(birdZ);
        }
    }

    /// <summary>Construit le plan des N blocs : Intro, Tuto 1, Tuto 2, aléatoires, Ending.</summary>
    public void BuildPlan()
    {
        int count = Mathf.Max(4, totalBlocks);
        slots = new BlockSlot[count];
        lastRandomPicked = null;

        List<GameObject> availablePool = null;
        if (noDuplicates)
        {
            availablePool = GetUniquePrefabsPool();
        }

        for (int i = 0; i < count; i++)
        {
            GameObject chosenPrefab;

            if (i == 0) chosenPrefab = introPrefab;
            else if (i == 1) chosenPrefab = tuto1Prefab;
            else if (i == 2) chosenPrefab = tuto2Prefab;
            else if (i == count - 1) chosenPrefab = endingPrefab;
            else
            {
                chosenPrefab = PickRandomBlock(ref availablePool);
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

    /// <summary>Liste des préfabs uniques et valides configurés dans randomBlockPrefabs.</summary>
    private List<GameObject> GetUniquePrefabsPool()
    {
        var pool = new List<GameObject>();
        if (randomBlockPrefabs == null) return pool;

        var seen = new HashSet<GameObject>();
        foreach (var prefab in randomBlockPrefabs)
        {
            if (prefab != null && seen.Add(prefab)) pool.Add(prefab);
        }

        return pool;
    }

    /// <summary>
    /// Tire un bloc au sort en évitant de répéter le précédent.
    /// Si noDuplicates, le tirage se fait sans remise dans availablePool (rechargé une fois épuisé).
    /// </summary>
    private GameObject PickRandomBlock(ref List<GameObject> availablePool)
    {
        if (randomBlockPrefabs == null || randomBlockPrefabs.Count == 0) return null;
        if (randomBlockPrefabs.Count == 1) return randomBlockPrefabs[0];

        if (noDuplicates)
        {
            if (availablePool == null || availablePool.Count == 0)
            {
                availablePool = GetUniquePrefabsPool();
                if (availablePool.Count == 0) return null;
            }

            int selectedIndex = -1;

            // Plusieurs choix restants : on évite de reprendre le bloc précédent.
            if (availablePool.Count > 1 && lastRandomPicked != null)
            {
                var validIndices = new List<int>();
                for (int i = 0; i < availablePool.Count; i++)
                {
                    if (availablePool[i] != lastRandomPicked) validIndices.Add(i);
                }

                if (validIndices.Count > 0)
                {
                    selectedIndex = validIndices[Random.Range(0, validIndices.Count)];
                }
            }

            if (selectedIndex < 0) selectedIndex = Random.Range(0, availablePool.Count);

            GameObject picked = availablePool[selectedIndex];
            availablePool.RemoveAt(selectedIndex);
            return picked;
        }

        // Tirage avec remise : on retente simplement tant qu'on retombe sur le bloc précédent.
        GameObject pickedStandard = null;
        int attempts = 10;
        while (attempts-- > 0)
        {
            pickedStandard = randomBlockPrefabs[Random.Range(0, randomBlockPrefabs.Count)];
            if (pickedStandard != lastRandomPicked) break;
        }

        return pickedStandard != null ? pickedStandard : randomBlockPrefabs[0];
    }

    private void UpdateStreaming(float birdZ)
    {
        if (slots == null) return;

        float maxSpawnZ = birdZ + (spawnAheadBlocks * blockInterval);

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.isDestroyed || slot.isSpawned) continue;

            // Blocs ordonnés en Z croissant : le premier trop loin arrête le parcours.
            if (slot.zPosition > maxSpawnZ) break;

            SpawnBlock(i);
        }
    }

    private void SpawnBlock(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        var slot = slots[index];
        if (slot.isSpawned || slot.isDestroyed || slot.prefab == null) return;

        Transform parent = blocksParent != null ? blocksParent : transform;
        GameObject instance = Instantiate(slot.prefab, new Vector3(0f, 0f, slot.zPosition),
            Quaternion.identity, parent);
        instance.name = $"[{slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
        slot.instance = instance;

        if (slot.prefab == endingPrefab ||
            instance.name.IndexOf("Ending", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            SetupEndingBlockNest(instance);
        }
    }

    /// <summary>Garantit qu'un BirdNestTrigger est présent sur le nid du bloc de fin.</summary>
    private void SetupEndingBlockNest(GameObject endingInstance)
    {
        if (endingInstance == null) return;
        if (endingInstance.GetComponentInChildren<BirdNestTrigger>(true) != null) return;

        Transform nest = endingInstance.transform.Find("Content/Bird Nest")
                      ?? endingInstance.transform.Find("Bird Nest");
        if (nest == null)
        {
            foreach (Transform child in endingInstance.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf("Nest", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    nest = child;
                    break;
                }
            }
        }

        if (nest == null) return;

        Collider col = nest.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider box = nest.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 3f, 4f);
            box.center = new Vector3(0f, 0.5f, 0f);
        }
        else
        {
            col.isTrigger = true;
        }

        nest.gameObject.AddComponent<BirdNestTrigger>();
    }

    private void UpdateDestruction(float birdZ)
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.isSpawned || slot.isDestroyed) continue;

            float blockExitZ = slot.zPosition + blockInterval;
            if (birdZ > blockExitZ + destroyDistanceBehind)
            {
                if (slot.instance != null) Destroy(slot.instance);
                slot.instance = null;
                slot.isDestroyed = true;
            }
        }
    }

    /// <summary>Brouillard linéaire calé sur la couleur de fond de la caméra.</summary>
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
        if (cam != null)
        {
            RenderSettings.fogColor = cam.backgroundColor;
            if (adjustCameraFarClip)
            {
                cam.farClipPlane = endDist + 20f;
            }
        }
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

    /// <summary>Supprime les blocs statiques laissés dans la scène, hors de ce générateur.</summary>
    private void CleanExistingSceneBlocks()
    {
        var existingBounds = Object.FindObjectsByType<BlockBounds>(FindObjectsSortMode.None);
        foreach (var b in existingBounds)
        {
            if (b == null || b.transform == transform || b.transform.IsChildOf(transform)) continue;
            if (blocksParent != null &&
                (b.transform == blocksParent || b.transform.IsChildOf(blocksParent))) continue;

            Destroy(b.gameObject);
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
        foreach (string name in randomNames)
        {
            GameObject go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(envPath + name);
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

        Transform parent = blocksParent != null ? blocksParent : transform;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.prefab == null) continue;

            GameObject instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(slot.prefab, parent);
            instance.transform.position = new Vector3(0f, 0f, slot.zPosition);
            instance.name = $"[PREVIEW {slot.index:D2}] {slot.prefab.name} (Z={slot.zPosition:F0})";
            slot.instance = instance;
        }

        Debug.Log($"[MapGenerator] Preview générée ({slots.Length} blocs).");
    }

    [ContextMenu("Nettoyer la Map (Éditeur)")]
    public void ClearMapEditor()
    {
        Transform parent = blocksParent != null ? blocksParent : transform;
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

    private void OnDrawGizmosSelected()
    {
        int count = Mathf.Max(4, totalBlocks);
        Gizmos.color = Color.green;

        for (int i = 0; i < count; i++)
        {
            float z = startZ + i * blockInterval;
            Vector3 center = new Vector3(3.9f, 5.5f, z + blockInterval * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(9.8f, 8f, blockInterval));
        }

        if (!enableFog) return;

        float birdZ = birdTransform != null ? birdTransform.position.z : startZ;
        Gizmos.color = Color.yellow;
        float fogStart = birdZ + fogStartBlocks * blockInterval;
        Gizmos.DrawLine(new Vector3(-5f, 5f, fogStart), new Vector3(15f, 5f, fogStart));

        Gizmos.color = Color.cyan;
        float fogEnd = birdZ + fogEndBlocks * blockInterval;
        Gizmos.DrawLine(new Vector3(-5f, 5f, fogEnd), new Vector3(15f, 5f, fogEnd));
    }
}
