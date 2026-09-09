using UnityEngine;

/// <summary>
/// Zone de spawn aléatoire pour cerceaux (KiBird).
/// - Définit un volume (zoneSize) dans lequel un cerceau apparaîtra de manière aléatoire.
/// - L'orientation du cerceau reste toujours fixée (face au joueur arrivant le long de l'axe Z).
/// - Peut être placée en plusieurs exemplaires dans chaque préfab de bloc environnemental.
/// - Se nettoie automatiquement quand le bloc est détruit par MapGenerator.
/// </summary>
[ExecuteAlways]
public class HoopSpawnZone : MonoBehaviour
{
    [Header("Cerceau à Spawner")]
    [Tooltip("Préfab du cerceau (Hoop.prefab). Si vide, chargé automatiquement depuis Assets/Prefabs/Hoop.prefab.")]
    public GameObject hoopPrefab;

    [Header("Dimensions de la Zone")]
    [Tooltip("Taille du volume dans lequel le cerceau peut apparaître (X = largeur, Y = hauteur, Z = profondeur).")]
    public Vector3 zoneSize = new Vector3(5f, 3.5f, 3f);

    [Header("Paramètres d'Apparition")]
    [Tooltip("Fait spawner le cerceau automatiquement au démarrage.")]
    public bool spawnOnStart = true;

    [Tooltip("Probabilité d'apparition du cerceau dans cette zone (1 = 100% garanti).")]
    [Range(0f, 1f)]
    public float spawnProbability = 1f;

    [Header("Orientation Fixée")]
    [Tooltip("Si activé, force l'orientation fixée vers le joueur (axe Z mondial).")]
    public bool keepFacingPlayer = true;

    [Tooltip("Rotation locale du cerceau garantissant qu'il fait face au joueur.")]
    public Vector3 fixedEulerRotation = new Vector3(0f, 90f, 90f);

    [Header("Gizmos Éditeur")]
    [Tooltip("Afficher la boîte de délimitation de la zone dans la scène.")]
    public bool showGizmos = true;

    [Tooltip("Couleur de la zone dans la vue Scène.")]
    public Color gizmoColor = new Color(1f, 0.84f, 0f, 0.35f); // Or translucide

    [System.NonSerialized]
    private GameObject spawnedHoopInstance;

    public GameObject SpawnedHoop => spawnedHoopInstance;

    private void Awake()
    {
        if (hoopPrefab == null)
        {
            LoadDefaultHoopPrefab();
        }
    }

    private void Start()
    {
        if (Application.isPlaying && spawnOnStart)
        {
            SpawnHoop();
        }
    }

    /// <summary>
    /// Instancie le cerceau à des coordonnées aléatoires à l'intérieur de la zone.
    /// </summary>
    public GameObject SpawnHoop()
    {
        if (hoopPrefab == null)
        {
            LoadDefaultHoopPrefab();
            if (hoopPrefab == null)
            {
                Debug.LogWarning("[HoopSpawnZone] Aucun préfab de cerceau assigné !", this);
                return null;
            }
        }

        // Test de probabilité d'apparition
        if (spawnProbability < 1f && Random.value > spawnProbability)
        {
            return null;
        }

        // Détruire un éventuel cerceau existant pour éviter les doublons
        if (spawnedHoopInstance != null)
        {
            if (Application.isPlaying) Destroy(spawnedHoopInstance);
            else DestroyImmediate(spawnedHoopInstance);
        }

        // Coordonnées aléatoires dans la boîte locale
        Vector3 randomLocalPos = new Vector3(
            Random.Range(-zoneSize.x * 0.5f, zoneSize.x * 0.5f),
            Random.Range(-zoneSize.y * 0.5f, zoneSize.y * 0.5f),
            Random.Range(-zoneSize.z * 0.5f, zoneSize.z * 0.5f)
        );

        Vector3 spawnWorldPos = transform.TransformPoint(randomLocalPos);

        // Orientation fixée face au joueur
        Quaternion spawnRotation = keepFacingPlayer
            ? Quaternion.Euler(fixedEulerRotation)
            : transform.rotation * Quaternion.Euler(fixedEulerRotation);

        // Instanciation sous ce Transform pour que le cerceau soit géré avec le bloc parent
        spawnedHoopInstance = Instantiate(hoopPrefab, spawnWorldPos, spawnRotation, transform);
        spawnedHoopInstance.name = $"Hoop_Spawned ({name})";

        return spawnedHoopInstance;
    }

    private void LoadDefaultHoopPrefab()
    {
#if UNITY_EDITOR
        hoopPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hoop.prefab");
#endif
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        // Volume translucide
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(Vector3.zero, zoneSize);

        // Contour fil de fer
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, zoneSize);

        // Repère au centre
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Vector3.zero, 0.3f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, zoneSize);
    }

#if UNITY_EDITOR
    [ContextMenu("Tester Spawn dans la Zone (Éditeur)")]
    public void TestSpawnEditor()
    {
        SpawnHoop();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    [ContextMenu("Nettoyer Cerceau de Test")]
    public void ClearTestSpawn()
    {
        if (spawnedHoopInstance != null)
        {
            DestroyImmediate(spawnedHoopInstance);
            spawnedHoopInstance = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
