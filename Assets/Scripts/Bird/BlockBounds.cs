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

    public const float StandardDefaultMinX = -0.97f;
    public const float StandardDefaultMaxX = 8.84f;
    public const float StandardDefaultMinHeight = 1.5f;
    public const float StandardDefaultMaxHeight = 9.5f;
    public const float StandardBlockLength = 48f;

    [Header("Limites Latérales (Montagnes / Clamping X)")]
    [Tooltip("Limite X gauche de la zone de vol.")]
    public float minX = -0.97f;

    [Tooltip("Limite X droite de la zone de vol.")]
    public float maxX = 8.84f;

    [Header("Limites Verticales (Altitude / Clamping Y)")]
    [Tooltip("Altitude maximale. Au plafond, l'oiseau replane automatiquement.")]
    public float maxHeight = 9.5f;

    [Tooltip("Altitude minimale (sol / surface de l'eau).")]
    public float minHeight = 1.5f;

    [Header("Plage Z du Bloc")]
    [Tooltip("Début du bloc en Z (coordonnées monde). Laisser 0 si autoZRange est activé.")]
    public float startZ;

    [Tooltip("Fin du bloc en Z (coordonnées monde).")]
    public float endZ;

    [Tooltip("Longueur standard du bloc le long de l'axe Z (48m dans KiBird).")]
    public float blockLength = StandardBlockLength;

    [Tooltip("Calcule automatiquement startZ et endZ depuis la position transform du bloc le long de Z.")]
    public bool autoZRange = true;

    [Header("Détection Automatique")]
    [Tooltip("Marge intérieure appliquée par rapport aux objets murs pour éviter de toucher les rochers.")]
    public float wallInnerMargin = 6.0f;

    public float CurrentWidth => Mathf.Abs(maxX - minX);
    public float CenterX => (minX + maxX) * 0.5f;

    public float StartZ => autoZRange ? transform.position.z : startZ;
    public float EndZ => autoZRange ? transform.position.z + blockLength : endZ;

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
        if (blockLength <= 0f) blockLength = StandardBlockLength;
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
        return z >= StartZ && z < EndZ;
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

    /// <summary>
    /// Élargit la zone horizontale autour de son centre actuel selon le facteur donné.
    /// Ex: factor = 1.5 pour +50%, factor = 2.0 pour doubler.
    /// </summary>
    public void ExpandWidth(float factor)
    {
        if (factor <= 0f) return;
        float center = CenterX;
        float halfWidth = (CurrentWidth * 0.5f) * factor;
        minX = center - halfWidth;
        maxX = center + halfWidth;
        UpdateZRange();
    }

    [ContextMenu("Élargir la largeur (+50%)")]
    public void ExpandWidth50Percent()
    {
        ExpandWidth(1.5f);
    }

    [ContextMenu("Élargir la largeur (+100% / Double)")]
    public void ExpandWidth100Percent()
    {
        ExpandWidth(2.0f);
    }

    [ContextMenu("Réinitialiser aux limites par défaut (-0.97 à 8.84)")]
    public void ResetToDefault()
    {
        minX = StandardDefaultMinX;
        maxX = StandardDefaultMaxX;
        minHeight = StandardDefaultMinHeight;
        maxHeight = StandardDefaultMaxHeight;
        blockLength = StandardBlockLength;
        autoZRange = true;
        UpdateZRange();
    }

    [ContextMenu("Auto-Détecter Limites depuis les Murs")]
    public void AutoDetectFromWalls()
    {
        Transform wallLeft = null;
        Transform wallRight = null;

        string[] leftNames = new string[] { "WallLeft", "WallsLeft", "LeftWall", "Wall_Left", "Left_Wall" };
        string[] rightNames = new string[] { "WallRight", "WallsRight", "RightWall", "Wall_Right", "Right_Wall" };

        Transform[] allChildren = GetComponentsInChildren<Transform>();

        foreach (var t in allChildren)
        {
            if (wallLeft == null)
            {
                foreach (var ln in leftNames)
                {
                    if (string.Equals(t.name, ln, System.StringComparison.OrdinalIgnoreCase))
                    {
                        wallLeft = t;
                        break;
                    }
                }
            }

            if (wallRight == null)
            {
                foreach (var rn in rightNames)
                {
                    if (string.Equals(t.name, rn, System.StringComparison.OrdinalIgnoreCase))
                    {
                        wallRight = t;
                        break;
                    }
                }
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
            Debug.Log($"[BlockBounds] Détection sur {gameObject.name}: minX={minX:F2}, maxX={maxX:F2} (largeur={CurrentWidth:F2}m)");
        }
        else
        {
            Debug.LogWarning($"[BlockBounds] Murs introuvables sur {gameObject.name}. Assurez-vous d'avoir des objets nommés WallLeft/WallsLeft et WallRight/WallsRight.");
        }

        UpdateZRange();
    }

    private void OnDrawGizmosSelected()
    {
        UpdateZRange();

        float curMinX = minX;
        float curMaxX = maxX;
        float curMinH = minHeight;
        float curMaxH = maxHeight;
        float curStartZ = StartZ;
        float curEndZ = EndZ;

        bool isWidened = CurrentWidth > (StandardDefaultMaxX - StandardDefaultMinX) * 1.2f;

        // Violet/Cyan si élargi, Vert sinon
        Gizmos.color = isWidened ? new Color(0.8f, 0.2f, 1f, 0.7f) : new Color(0f, 1f, 0.4f, 0.6f);
        Vector3 center = new Vector3((curMinX + curMaxX) * 0.5f, (curMinH + curMaxH) * 0.5f, (curStartZ + curEndZ) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(curMaxX - curMinX), Mathf.Abs(curMaxH - curMinH), Mathf.Abs(curEndZ - curStartZ));
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(curMinX, curMaxH, curStartZ), new Vector3(curMaxX, curMaxH, curStartZ));
        Gizmos.DrawLine(new Vector3(curMaxX, curMaxH, curStartZ), new Vector3(curMaxX, curMaxH, curEndZ));
        Gizmos.DrawLine(new Vector3(curMaxX, curMaxH, curEndZ), new Vector3(curMinX, curMaxH, curEndZ));
        Gizmos.DrawLine(new Vector3(curMinX, curMaxH, curEndZ), new Vector3(curMinX, curMaxH, curStartZ));
    }
}
