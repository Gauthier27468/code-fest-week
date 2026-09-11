using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zone de vol d'un bloc : limites latérales et d'altitude. MoveBird s'y adapte quand l'oiseau
/// traverse le bloc (option useBlockBounds). Le bloc s'étend sur blockLength mètres en Z à
/// partir de sa position.
/// </summary>
[SelectionBase]
public class BlockBounds : MonoBehaviour
{
    public const float StandardDefaultMinX = -0.97f;
    public const float StandardDefaultMaxX = 8.84f;
    public const float StandardDefaultMinHeight = 1.5f;
    public const float StandardDefaultMaxHeight = 9.5f;
    public const float StandardBlockLength = 48f;

    /// <summary>Tous les BlockBounds actifs, pour la recherche par position Z.</summary>
    private static readonly List<BlockBounds> AllBounds = new List<BlockBounds>();

    [Header("Limites Latérales (Montagnes / Clamping X)")]
    [Tooltip("Limite X gauche de la zone de vol.")]
    public float minX = StandardDefaultMinX;

    [Tooltip("Limite X droite de la zone de vol.")]
    public float maxX = StandardDefaultMaxX;

    [Header("Limites Verticales (Altitude / Clamping Y)")]
    [Tooltip("Altitude maximale. Au plafond, l'oiseau replane automatiquement.")]
    public float maxHeight = StandardDefaultMaxHeight;

    [Tooltip("Altitude minimale (sol / surface de l'eau).")]
    public float minHeight = StandardDefaultMinHeight;

    [Header("Longueur du Bloc")]
    [Tooltip("Longueur du bloc le long de l'axe Z (48 m dans KiBird).")]
    [Min(1f)] public float blockLength = StandardBlockLength;

    [Header("Détection Automatique")]
    [Tooltip("Marge intérieure appliquée par rapport aux murs pour éviter de toucher les rochers.")]
    public float wallInnerMargin = 6.0f;

    public float CurrentWidth => Mathf.Abs(maxX - minX);
    public float CenterX => (minX + maxX) * 0.5f;
    public float StartZ => transform.position.z;
    public float EndZ => transform.position.z + blockLength;

    private void OnEnable() => AllBounds.Add(this);
    private void OnDisable() => AllBounds.Remove(this);

    public bool ContainsZ(float z) => z >= StartZ && z < EndZ;

    /// <summary>BlockBounds actif pour une position Z donnée, null s'il n'y en a pas.</summary>
    public static BlockBounds GetBoundsAtZ(float z)
    {
        foreach (BlockBounds bounds in AllBounds)
        {
            if (bounds.ContainsZ(z)) return bounds;
        }
        return null;
    }

    // ------------------------------------------------------------------ outils éditeur (BlockBoundsEditor)

    /// <summary>Élargit la zone horizontale autour de son centre (1.5 = +50 %, 2 = double).</summary>
    public void ExpandWidth(float factor)
    {
        if (factor <= 0f) return;
        float center = CenterX;
        float halfWidth = CurrentWidth * 0.5f * factor;
        minX = center - halfWidth;
        maxX = center + halfWidth;
    }

    [ContextMenu("Réinitialiser aux limites par défaut")]
    public void ResetToDefault()
    {
        minX = StandardDefaultMinX;
        maxX = StandardDefaultMaxX;
        minHeight = StandardDefaultMinHeight;
        maxHeight = StandardDefaultMaxHeight;
        blockLength = StandardBlockLength;
    }

    /// <summary>Déduit minX / maxX de la position des murs enfants (WallLeft / WallRight...).</summary>
    [ContextMenu("Auto-Détecter Limites depuis les Murs")]
    public void AutoDetectFromWalls()
    {
        string[] leftNames = { "WallLeft", "WallsLeft", "LeftWall", "Wall_Left", "Left_Wall" };
        string[] rightNames = { "WallRight", "WallsRight", "RightWall", "Wall_Right", "Right_Wall" };

        Transform wallLeft = null;
        Transform wallRight = null;
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (wallLeft == null && HasAnyName(t, leftNames)) wallLeft = t;
            if (wallRight == null && HasAnyName(t, rightNames)) wallRight = t;
        }

        if (wallLeft == null || wallRight == null)
        {
            Debug.LogWarning($"[BlockBounds] Murs introuvables sur {name}. Nommer les objets WallLeft/WallsLeft et WallRight/WallsRight.");
            return;
        }

        float leftWallX = Mathf.Min(wallLeft.position.x, wallRight.position.x);
        float rightWallX = Mathf.Max(wallLeft.position.x, wallRight.position.x);
        minX = leftWallX + wallInnerMargin;
        maxX = rightWallX - (wallInnerMargin - 0.5f);
        Debug.Log($"[BlockBounds] Détection sur {name} : minX={minX:F2}, maxX={maxX:F2} (largeur={CurrentWidth:F2}m)");
    }

    private static bool HasAnyName(Transform t, string[] names)
    {
        foreach (string candidate in names)
        {
            if (string.Equals(t.name, candidate, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Zone de vol (verte, violette si élargie) et plafond (jaune) dans la vue Scène.</summary>
    private void OnDrawGizmosSelected()
    {
        bool isWidened = CurrentWidth > (StandardDefaultMaxX - StandardDefaultMinX) * 1.2f;
        Gizmos.color = isWidened ? new Color(0.8f, 0.2f, 1f, 0.7f) : new Color(0f, 1f, 0.4f, 0.6f);

        float startZ = StartZ;
        float endZ = EndZ;
        Gizmos.DrawWireCube(
            new Vector3(CenterX, (minHeight + maxHeight) * 0.5f, (startZ + endZ) * 0.5f),
            new Vector3(CurrentWidth, Mathf.Abs(maxHeight - minHeight), blockLength));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(minX, maxHeight, startZ), new Vector3(maxX, maxHeight, startZ));
        Gizmos.DrawLine(new Vector3(maxX, maxHeight, startZ), new Vector3(maxX, maxHeight, endZ));
        Gizmos.DrawLine(new Vector3(maxX, maxHeight, endZ), new Vector3(minX, maxHeight, endZ));
        Gizmos.DrawLine(new Vector3(minX, maxHeight, endZ), new Vector3(minX, maxHeight, startZ));
    }
}
