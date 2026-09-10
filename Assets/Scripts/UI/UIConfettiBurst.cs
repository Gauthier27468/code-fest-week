using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Confettis en Image UI (pas un ParticleSystem 3D) : garantit un rendu AU PREMIER PLAN,
/// localisé à l'origine donnée. ConfettiEffect (ParticleSystem 3D rendu par la caméra) se
/// retrouve toujours DERRIÈRE un Canvas Screen Space - Overlay, qui s'affiche après tout
/// rendu caméra par construction — inutilisable ici, d'où cette variante 100% UI.
/// </summary>
public class UIConfettiBurst : MonoBehaviour
{
    private static readonly Color[] Palette =
    {
        new Color(1.0f, 0.15f, 0.22f), // Rouge vif
        new Color(1.0f, 0.52f, 0.05f), // Orange éclatant
        new Color(1.0f, 0.88f, 0.10f), // Jaune / Or
        new Color(0.12f, 0.92f, 0.36f), // Vert émeraude
        new Color(0.05f, 0.82f, 1.00f), // Cyan vibrant
        new Color(0.28f, 0.45f, 1.00f), // Bleu royal
        new Color(0.96f, 0.18f, 0.82f), // Rose magenta
    };

    private class Piece
    {
        public RectTransform rt;
        public Image image;
        public Vector2 velocity;
        public float angularVelocity;
        public float age;
        public float lifetime;
    }

    private readonly List<Piece> pieces = new List<Piece>();

    /// <summary>
    /// Fait exploser des confettis autour du centre de <paramref name="originRect"/>, dans le
    /// même parent que lui, juste avant son index : devant le reste du panneau, mais derrière
    /// ce texte/élément (qui reste le dernier dessiné, donc au premier plan).
    /// </summary>
    public static void SpawnBurst(RectTransform originRect, int count = 36)
    {
        if (originRect == null) return;
        RectTransform parent = originRect.parent as RectTransform;
        if (parent == null) return;

        var go = new GameObject("UIConfettiBurst", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetSiblingIndex(originRect.GetSiblingIndex()); // Juste avant le texte -> derrière lui.

        // Même parent que le texte : son anchoredPosition est directement dans le bon repère,
        // pas besoin de conversion écran/monde.
        Vector2 localOrigin = originRect.anchoredPosition;

        go.AddComponent<UIConfettiBurst>().Begin(localOrigin, count);
    }

    private void Begin(Vector2 origin, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var pieceGO = new GameObject("Confetto", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)pieceGO.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Random.Range(20f, 32f), Random.Range(40f, 68f));
            rt.anchoredPosition = origin;
            rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));

            Image img = pieceGO.GetComponent<Image>();
            img.color = Palette[Random.Range(0, Palette.Length)];
            img.raycastTarget = false;

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(500f, 950f);

            pieces.Add(new Piece
            {
                rt = rt,
                image = img,
                velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                angularVelocity = Random.Range(-540f, 540f),
                age = 0f,
                lifetime = Random.Range(1.0f, 1.6f)
            });
        }
    }

    private void Update()
    {
        // Plafonné : sans ça, un gros pic de temps réel entre deux frames (ex. le délai
        // pendant lequel un menu contextuel de l'Éditeur reste ouvert) vieillit tous les
        // confettis d'un coup dès la première frame et détruit la salve instantanément.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        bool anyAlive = false;

        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            Piece p = pieces[i];
            p.age += dt;
            if (p.age >= p.lifetime)
            {
                Destroy(p.rt.gameObject);
                pieces.RemoveAt(i);
                continue;
            }

            anyAlive = true;
            p.velocity += Vector2.down * 650f * dt; // Gravité
            p.velocity *= 1f - Mathf.Clamp01(1.6f * dt); // Frottement aérodynamique
            p.rt.anchoredPosition += p.velocity * dt;
            p.rt.localEulerAngles += new Vector3(0f, 0f, p.angularVelocity * dt);

            float t = p.age / p.lifetime;
            if (t > 0.7f)
            {
                Color c = p.image.color;
                c.a = Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
                p.image.color = c;
            }
        }

        if (!anyAlive) Destroy(gameObject);
    }
}
