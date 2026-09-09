using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zone de vol d'un bloc : limites latérales et d'altitude. MoveBird s'y adapte quand l'oiseau
/// traverse le bloc, à condition que son option useBlockBounds soit activée.
/// </summary>
[SelectionBase]
public class BlockBounds : MonoBehaviour
{
    private static readonly List<BlockBounds> AllBounds = new List<BlockBounds>();

    public static IReadOnlyList<BlockBounds> ActiveBounds => AllBounds;

    [Header("Limites Latérales (Montagnes)")]
    [Tooltip("Limite X gauche de la zone de vol.")]
    public float minX = -0.97f;

    [Tooltip("Limite X droite de la zone de vol.")]
    public float maxX = 8.84f;

    [Header("Limites Verticales (Altitude)")]
    [Tooltip("Altitude maximale. Au plafond, l'oiseau replane automatiquement.")]
    public float maxHeight = 9.5f;

    [Tooltip("Altitude minimale (sol / surface de l'eau).")]
    public float minHeight = 1.5f;

    [Header("Plage Z du Bloc")]
    [Tooltip("Début du bloc en Z (coordonnées monde). Laisser 0 si autoZRange est activé.")]
    public float startZ;

    [Tooltip("Fin du bloc en Z (coordonnées monde).")]
    public float endZ;

    [Tooltip("Longueur par défaut du bloc le long de l'axe Z si endZ n'est pas spécifié.")]
    public float blockLength = 50f;

    [Tooltip("Calcule automatiquement startZ et endZ depuis la position transform du bloc.")]
    public bool autoZRange = true;

    [Header("Détection Automatique")]
    [Tooltip("Marge intérieure appliquée par rapport aux objets WallLeft et WallRight pour éviter de toucher les rochers.")]
    public float wallInnerMargin = 6.0f;

    private void OnEnable()
    {
        if (!AllBounds.Contains(this))
        {
            AllBounds.Add(this);
        }
        UpdateZRange();
    }

    private void OnDisable()
    {
        AllBounds.Remove(this);
    }

    private void Awake()
    {
        UpdateZRange();
    }

    private void OnValidate()
    {
        UpdateZRange();
    }

    public void UpdateZRange()
    {
        if (autoZRange)
        {
            startZ = transform.position.z;
            endZ = startZ + blockLength;
        }
    }

    public bool ContainsZ(float z)
    {
        return z >= startZ && z < endZ;
    }

    /// <summary>BlockBounds actif pour une position Z donnée, null s'il n'y en a pas.</summary>
    public static BlockBounds GetBoundsAtZ(float z)
    {
        for (int i = 0; i < AllBounds.Count; i++)
        {
            var b = AllBounds[i];
            if (b != null && b.ContainsZ(z))
            {
                return b;
            }
        }
        return null;
    }

    [ContextMenu("Auto-Détecter Limites depuis WallLeft/WallRight")]
    public void AutoDetectFromWalls()
    {
        Transform wallLeft = transform.Find("Content/WallLeft");
        Transform wallRight = transform.Find("Content/WallRight");

        if (wallLeft == null || wallRight == null)
        {
            Transform[] children = GetComponentsInChildren<Transform>();
            foreach (var t in children)
            {
                if (t.name == "WallLeft" && wallLeft == null) wallLeft = t;
                if (t.name == "WallRight" && wallRight == null) wallRight = t;
            }
        }

        if (wallLeft != null && wallRight != null)
        {
            float posX1 = wallRight.position.x;
            float posX2 = wallLeft.position.x;
            float leftWallWorldX = Mathf.Min(posX1, posX2);
            float rightWallWorldX = Mathf.Max(posX1, posX2);

            minX = leftWallWorldX + wallInnerMargin;
            maxX = rightWallWorldX - (wallInnerMargin - 0.5f);
        }

        UpdateZRange();
    }

    private void OnDrawGizmosSelected()
    {
        UpdateZRange();

        Gizmos.color = new Color(0f, 1f, 0.4f, 0.6f);
        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minHeight + maxHeight) * 0.5f, (startZ + endZ) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(maxX - minX), Mathf.Abs(maxHeight - minHeight), Mathf.Abs(endZ - startZ));
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, maxHeight, startZ), new Vector3(maxX, maxHeight, startZ));
        Gizmos.DrawLine(new Vector3(maxX, maxHeight, startZ), new Vector3(maxX, maxHeight, endZ));
        Gizmos.DrawLine(new Vector3(maxX, maxHeight, endZ), new Vector3(minX, maxHeight, endZ));
        Gizmos.DrawLine(new Vector3(minX, maxHeight, endZ), new Vector3(minX, maxHeight, startZ));
    }
}
