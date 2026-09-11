using UnityEngine;

/// <summary>
/// Volume dans lequel un cerceau apparaît à une position aléatoire, toujours orienté face au
/// joueur. Plusieurs zones peuvent être posées dans un même préfab de bloc.
/// </summary>
public class HoopSpawnZone : MonoBehaviour
{
    [Header("Cerceau à Spawner")]
    [Tooltip("Préfab du cerceau (Hoop.prefab).")]
    public GameObject hoopPrefab;

    [Header("Dimensions de la Zone")]
    [Tooltip("Taille du volume dans lequel le cerceau peut apparaître (X = largeur, Y = hauteur, Z = profondeur).")]
    public Vector3 zoneSize = new Vector3(5f, 3.5f, 3f);

    [Header("Paramètres d'Apparition")]
    [Tooltip("Fait apparaître le cerceau automatiquement au démarrage.")]
    public bool spawnOnStart = true;

    [Tooltip("Probabilité d'apparition du cerceau dans cette zone (1 = 100% garanti).")]
    [Range(0f, 1f)]
    public float spawnProbability = 1f;

    [Header("Orientation Fixée")]
    [Tooltip("Oriente le cerceau selon l'axe Z du monde (face au joueur), quelle que soit la rotation de la zone.")]
    public bool keepFacingPlayer = true;

    [Tooltip("Rotation du cerceau garantissant qu'il fait face au joueur.")]
    public Vector3 fixedEulerRotation = new Vector3(0f, 90f, 90f);

    [Header("Gizmos Éditeur")]
    [Tooltip("Afficher la boîte de délimitation de la zone dans la scène.")]
    public bool showGizmos = true;

    [Tooltip("Couleur de la zone dans la vue Scène.")]
    public Color gizmoColor = new Color(1f, 0.84f, 0f, 0.35f);

    private GameObject spawnedHoop;

    private void Start()
    {
        if (spawnOnStart) SpawnHoop();
    }

    /// <summary>Instancie le cerceau à une position aléatoire dans la zone (remplace le précédent).</summary>
    public GameObject SpawnHoop()
    {
        if (hoopPrefab == null)
        {
            Debug.LogWarning("[HoopSpawnZone] Aucun préfab de cerceau assigné !", this);
            return null;
        }

        if (Random.value > spawnProbability) return null;

        if (spawnedHoop != null)
        {
            if (Application.isPlaying) Destroy(spawnedHoop);
            else DestroyImmediate(spawnedHoop);
        }

        Vector3 randomLocalPos = new Vector3(
            Random.Range(-zoneSize.x * 0.5f, zoneSize.x * 0.5f),
            Random.Range(-zoneSize.y * 0.5f, zoneSize.y * 0.5f),
            Random.Range(-zoneSize.z * 0.5f, zoneSize.z * 0.5f));

        Quaternion rotation = keepFacingPlayer
            ? Quaternion.Euler(fixedEulerRotation)
            : transform.rotation * Quaternion.Euler(fixedEulerRotation);

        spawnedHoop = Instantiate(hoopPrefab, transform.TransformPoint(randomLocalPos), rotation, transform);
        spawnedHoop.name = $"Hoop_Spawned ({name})";
        return spawnedHoop;
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(Vector3.zero, zoneSize);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, zoneSize);

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
    private void TestSpawnEditor()
    {
        SpawnHoop();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    [ContextMenu("Nettoyer Cerceau de Test")]
    private void ClearTestSpawn()
    {
        if (spawnedHoop == null) return;

        DestroyImmediate(spawnedHoop);
        spawnedHoop = null;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
