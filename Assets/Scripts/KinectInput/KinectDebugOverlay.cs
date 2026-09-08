using UnityEngine;

/// <summary>
/// Overlay de vérification du flux Kinect : état du bridge, distance, latence, barres des
/// quatre commandes et squelette 2D. Volontairement en OnGUI() : aucun Canvas, aucune police,
/// aucun prefab à préparer — on pose le composant sur un GameObject et ça marche.
///
/// À désactiver pour la JPO (c'est un outil de mise au point, pas une UI joueur).
/// </summary>
[RequireComponent(typeof(KinectInputSource))]
public class KinectDebugOverlay : MonoBehaviour
{
    [Tooltip("Raccourci pour afficher/masquer l'overlay pendant une partie.")]
    public KeyCode toggleKey = KeyCode.F1;

    public bool visible = true;

    [Tooltip("Affiche le squelette reçu, en plus des jauges.")]
    public bool drawSkeleton = true;

    private KinectInputSource _source;
    private Texture2D _pixel;
    private GUIStyle _label;

    // Os à relier pour dessiner le squelette (index dans KinectInputSource.JointNames).
    private static readonly int[,] Bones =
    {
        { 1, 2 }, // épaules
        { 1, 3 }, { 3, 5 }, // bras gauche
        { 2, 4 }, { 4, 6 }, // bras droit
        { 1, 7 }, { 2, 8 }, // buste
        { 7, 8 }, // hanches
    };

    private void Awake()
    {
        _source = GetComponent<KinectInputSource>();
        _pixel = new Texture2D(1, 1);
        _pixel.SetPixel(0, 0, Color.white);
        _pixel.Apply();
    }

    private void OnDestroy()
    {
        if (_pixel != null) Destroy(_pixel);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
    }

    private void OnGUI()
    {
        if (!visible) return;

        _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

        const float w = 340f;
        float h = drawSkeleton ? 400f : 210f;
        GUILayout.BeginArea(new Rect(10, 10, w, h), GUI.skin.box);

        if (!_source.BridgeAlive)
        {
            GUILayout.Label("<color=#ff5555><b>BRIDGE HORS LIGNE</b></color>", _label);
            GUILayout.Label("Aucun paquet reçu. Le script Python tourne-t-il ?\n" +
                            "→ ./run_bridge.sh", _label);
            GUILayout.EndArea();
            return;
        }

        string state = _source.HasPlayer
            ? "<color=#55ff55><b>JOUEUR DÉTECTÉ</b></color>"
            : (_source.InZone
                ? "<color=#ffaa00><b>joueur hors zone</b></color>"
                : "<color=#ffaa00><b>en attente d'un joueur</b></color>");
        GUILayout.Label(state + (_source.ReplayMode ? "  <i>(replay)</i>" : ""), _label);

        GUILayout.Label($"Distance : <b>{_source.Distance:F2} m</b>", _label);

        // La latence bout-en-bout est un critère de réussite du projet (< 150 ms).
        string latColor = _source.LatencyMs < 150f ? "#55ff55" : "#ff5555";
        GUILayout.Label($"Latence : <color={latColor}><b>{_source.LatencyMs:F0} ms</b></color>   " +
                        $"seq {_source.Sequence}", _label);

        GUILayout.Space(6);
        Vector3 input = _source.Input;
        DrawBar("Gauche/Droite", input.x, -1f, 1f);
        DrawBar("Altitude", input.y, -1f, 1f);
        DrawBar("Vitesse", input.z, -1f, 1f);
        DrawBar("Plané", _source.Glide, 0f, 1f);

        if (drawSkeleton)
        {
            GUILayout.Space(8);
            GUILayout.Label("Squelette :", _label);
            DrawSkeleton(GUILayoutUtility.GetRect(w - 20f, 170f));
        }

        GUILayout.EndArea();
    }

    private void DrawBar(string label, float value, float min, float max)
    {
        GUILayout.Label($"{label} : {value:+0.00;-0.00; 0.00}", _label);
        Rect r = GUILayoutUtility.GetRect(1, 12);
        GUI.DrawTexture(r, _pixel, ScaleMode.StretchToFill, false, 0, new Color(0.2f, 0.2f, 0.2f), 0, 0);

        float t = Mathf.InverseLerp(min, max, value);
        if (min < 0f)
        {
            // Barre bipolaire : on remplit depuis le centre, ce qui rend le signe lisible d'un coup d'œil.
            float centre = r.x + r.width * 0.5f;
            float end = r.x + r.width * t;
            var fill = new Rect(Mathf.Min(centre, end), r.y, Mathf.Abs(end - centre), r.height);
            GUI.DrawTexture(fill, _pixel, ScaleMode.StretchToFill, false, 0, new Color(0.3f, 0.8f, 1f), 0, 0);
        }
        else
        {
            var fill = new Rect(r.x, r.y, r.width * t, r.height);
            GUI.DrawTexture(fill, _pixel, ScaleMode.StretchToFill, false, 0, new Color(0.4f, 1f, 0.5f), 0, 0);
        }
    }

    private void DrawSkeleton(Rect area)
    {
        GUI.DrawTexture(area, _pixel, ScaleMode.StretchToFill, false, 0, new Color(0.1f, 0.1f, 0.1f), 0, 0);
        var joints = _source.Joints;
        if (joints == null || !_source.HasPlayer) return;

        // Les coordonnées arrivent normalisées dans le repère image (origine en haut à gauche).
        Vector2 ToScreen(int i) => new Vector2(
            area.x + joints[i].X * area.width,
            area.y + joints[i].Y * area.height);

        for (int b = 0; b < Bones.GetLength(0); b++)
        {
            int a = Bones[b, 0], c = Bones[b, 1];
            if (joints[a].Confidence < 0.5f || joints[c].Confidence < 0.5f) continue;
            DrawLine(ToScreen(a), ToScreen(c), new Color(0.5f, 0.9f, 1f));
        }

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i].Confidence < 0.5f) continue;
            Vector2 p = ToScreen(i);
            GUI.DrawTexture(new Rect(p.x - 2.5f, p.y - 2.5f, 5, 5), _pixel,
                ScaleMode.StretchToFill, false, 0, Color.white, 0, 0);
        }
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color)
    {
        Vector2 d = b - a;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - 1f, d.magnitude, 2f), _pixel,
            ScaleMode.StretchToFill, false, 0, color, 0, 0);
        GUI.matrix = saved;
    }
}
