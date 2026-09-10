using UnityEngine;

namespace KiBird.FX
{
    /// <summary>
    /// Souffle d'air autour de l'oiseau : tourbillons en bout d'aile (rubans + bouffées lâchées à
    /// chaque battement) et filets d'air qui défilent le long du corps quand la vitesse monte.
    ///
    /// Tout est construit une fois au démarrage puis seulement modulé en intensité : aucune
    /// allocation par frame. L'intensité est déduite du mouvement réel (vitesse du Transform,
    /// déplacement des os de bout d'aile), donc l'effet suit le jeu qu'il soit piloté par la
    /// Kinect, par le clavier ou par une animation, sans être câblé à MoveBird.
    /// </summary>
    [DisallowMultipleComponent]
    public class WingWindFX : MonoBehaviour
    {
        [Header("Bouts d'ailes")]
        [Tooltip("Laisser vide : les os sont retrouvés par leur nom dans la hiérarchie de l'oiseau.")]
        public Transform leftWingTip;
        public Transform rightWingTip;
        [SerializeField] private string leftWingTipName = "Bird_LeftWing_Tip";
        [SerializeField] private string rightWingTipName = "Bird_RightWing_Tip";

        [Header("Tourbillons de bout d'aile")]
        public bool enableWingtipVortices = true;

        [Tooltip("Longueur du ruban en secondes : plus c'est haut, plus la traînée est longue.")]
        public float trailTime = 0.35f;

        [Tooltip("Largeur du ruban en unités monde, à pleine intensité.")]
        public float trailWidth = 0.13f;

        public Color trailColor = new Color(0.87f, 0.94f, 1f, 0.55f);

        [Header("Bouffées d'air au battement")]
        public bool enableFlapPuffs = true;

        [Tooltip("Nombre de particules lâchées à chaque coup d'aile descendant.")]
        [Range(0, 30)] public int particlesPerFlap = 7;

        [Tooltip("Vitesse du bout d'aile (u/s, mouvement propre de l'aile) valant un battement à pleine puissance.")]
        public float flapSpeedReference = 3f;

        [Tooltip("Relève automatiquement la référence ci-dessus sur le battement le plus ample observé. " +
                 "À laisser coché : l'effet s'adapte tout seul à l'animation utilisée.")]
        public bool autoCalibrateFlap = true;

        [Header("Filets d'air (sensation de vitesse)")]
        public bool enableAirflowStreaks = true;

        [Tooltip("Distance devant l'oiseau où les filets d'air apparaissent.")]
        public float airflowSpawnDistance = 12f;

        [Tooltip("Rayon de l'anneau d'apparition : creux au centre pour ne pas encombrer la vue.")]
        public float airflowRadius = 2.9f;

        [Tooltip("Nombre de filets par seconde à pleine vitesse.")]
        public float airflowMaxRate = 40f;

        public Color airflowColor = new Color(0.9f, 0.96f, 1f, 0.32f);

        [Header("Dosage de l'intensité")]
        [Tooltip("Vitesse d'avance (u/s) à laquelle le souffle est au maximum.")]
        public float referenceForwardSpeed = 12f;

        [Tooltip("Vitesse latérale (u/s) d'un virage franc : accentue le souffle de l'aile extérieure.")]
        public float referenceTurnSpeed = 6f;

        [Tooltip("Vitesse de chute (u/s) d'un piqué franc : accentue le souffle général.")]
        public float referenceDiveSpeed = 5f;

        [Tooltip("En dessous de cette vitesse, plus aucun souffle (oiseau à l'arrêt / menu).")]
        public float minVisibleSpeed = 1.5f;

        [Tooltip("Réactivité : bas = souffle mou et paresseux, haut = réagit au quart de tour.")]
        public float intensitySmoothing = 7f;

        /// <summary>Ressources d'un bout d'aile : ruban, bouffées, et suivi du mouvement de l'os.</summary>
        private sealed class WingFX
        {
            public Transform tip;
            public float side;                  // -1 = aile gauche, +1 = aile droite
            public Transform anchor;            // objet racine repositionné sur le bout d'aile
            public TrailRenderer trail;
            public ParticleSystem puff;
            public ParticleSystem.EmissionModule puffEmission;
            public Vector3 lastTipWorld;
            public bool hasLastTip;
            public float flapCooldown;
        }

        private WingFX left;
        private WingFX right;

        private ParticleSystem airflow;
        private ParticleSystem.EmissionModule airflowEmission;
        private Transform airflowAnchor;

        private Vector3 lastPosition;
        private float intensity;
        private float observedFlapPeak;

        private const float FlapBurstCooldown = 0.12f;

        private void Start()
        {
            lastPosition = transform.position;

            if (enableWingtipVortices || enableFlapPuffs)
            {
                ResolveWingTips();
                left = BuildWing("WindFX_LeftWingtip", leftWingTip, -1f);
                right = BuildWing("WindFX_RightWingtip", rightWingTip, 1f);
            }

            if (enableAirflowStreaks)
            {
                BuildAirflow();
            }
        }

        private void OnDestroy()
        {
            DestroyAnchor(left);
            DestroyAnchor(right);
            if (airflowAnchor != null) Destroy(airflowAnchor.gameObject);
        }

        private static void DestroyAnchor(WingFX wing)
        {
            if (wing != null && wing.anchor != null) Destroy(wing.anchor.gameObject);
        }

        /// <summary>
        /// Exécuté en LateUpdate : à ce moment l'oiseau a déjà été déplacé (Update de MoveBird) et
        /// l'Animator a posé les os, donc les bouts d'ailes sont lus à leur position finale.
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            Vector3 velocity = (position - lastPosition) / dt;
            lastPosition = position;

            bool grounded = MoveBird.IsDead || MoveBird.IsWon;
            float target = grounded ? 0f : ComputeBodyWind(velocity);
            // Lissage indépendant du framerate : même montée en intensité à 60 comme à 144 fps.
            intensity = Mathf.Lerp(intensity, target, 1f - Mathf.Exp(-intensitySmoothing * dt));

            UpdateWing(left, velocity, dt, grounded);
            UpdateWing(right, velocity, dt, grounded);
            UpdateAirflow(position, velocity);
        }

        /// <summary>Souffle dû au corps seul : avance, virage et piqué, sans le battement d'ailes.</summary>
        private float ComputeBodyWind(Vector3 velocity)
        {
            float forward01 = Mathf.Clamp01(velocity.z / Mathf.Max(0.01f, referenceForwardSpeed));
            float turn01 = Mathf.Clamp01(Mathf.Abs(velocity.x) / Mathf.Max(0.01f, referenceTurnSpeed));
            float dive01 = Mathf.Clamp01(-velocity.y / Mathf.Max(0.01f, referenceDiveSpeed));

            float wind = Mathf.Clamp01(forward01 + turn01 * 0.45f + dive01 * 0.55f);

            // Coupure nette à l'arrêt : pas de traînée sur un oiseau immobile (menu, pause, nid).
            return wind * Mathf.Clamp01(velocity.magnitude / Mathf.Max(0.01f, minVisibleSpeed));
        }

        private void UpdateWing(WingFX wing, Vector3 velocity, float dt, bool grounded)
        {
            if (wing == null || wing.tip == null) return;

            Vector3 tipWorld = wing.tip.position;
            wing.anchor.position = tipWorld;

            // Mouvement propre de l'aile : on retire l'avance du corps du déplacement du bout
            // d'aile, puis on exprime le reste dans le repère de l'oiseau. Il ne subsiste que le
            // battement (et le roulis d'un virage appuyé, qui brasse aussi de l'air).
            // InverseTransformDirection et non InverseTransformPoint : le premier ignore l'échelle,
            // et l'oiseau en porte une de 10 qui diviserait la vitesse mesurée d'autant.
            float flap01 = 0f;
            bool downstroke = false;

            if (wing.hasLastTip)
            {
                Vector3 relative = (tipWorld - wing.lastTipWorld) / dt - velocity;
                Vector3 localVelocity = transform.InverseTransformDirection(relative);
                float tipSpeed = localVelocity.magnitude;

                if (autoCalibrateFlap && !grounded && tipSpeed > observedFlapPeak)
                {
                    observedFlapPeak = tipSpeed;
                }

                float reference = Mathf.Max(flapSpeedReference, observedFlapPeak * 0.85f);
                flap01 = Mathf.Clamp01(tipSpeed / Mathf.Max(0.01f, reference));
                downstroke = localVelocity.y < -reference * 0.5f;
            }
            wing.lastTipWorld = tipWorld;
            wing.hasLastTip = true;

            // L'aile extérieure au virage brasse plus d'air que l'aile intérieure.
            float outerBoost = Mathf.Clamp01(-wing.side * velocity.x / Mathf.Max(0.01f, referenceTurnSpeed));
            float wingIntensity = grounded
                ? 0f
                : Mathf.Clamp01(intensity * (0.85f + outerBoost * 0.35f) + flap01 * 0.55f);

            if (wing.trail != null)
            {
                wing.trail.widthMultiplier = trailWidth * (0.35f + 0.65f * wingIntensity);
                wing.trail.time = trailTime * (0.55f + 0.45f * wingIntensity);

                Color head = trailColor;
                head.a = trailColor.a * wingIntensity;
                wing.trail.startColor = head;
                head.a = 0f;
                wing.trail.endColor = head;

                wing.trail.emitting = wingIntensity > 0.02f;
            }

            if (wing.puff != null)
            {
                wing.puffEmission.rateOverTimeMultiplier = grounded ? 0f : 5f * wingIntensity;

                wing.flapCooldown -= dt;
                if (downstroke && wing.flapCooldown <= 0f && particlesPerFlap > 0 && !grounded)
                {
                    wing.puff.Emit(Mathf.RoundToInt(particlesPerFlap * Mathf.Max(0.35f, wingIntensity)));
                    wing.flapCooldown = FlapBurstCooldown;
                }
            }
        }

        private void UpdateAirflow(Vector3 position, Vector3 velocity)
        {
            if (airflow == null) return;

            // L'anneau d'apparition reste devant l'oiseau : celui-ci fonce dedans et traverse les
            // filets d'air, qui défilent donc naturellement de part et d'autre de la caméra.
            airflowAnchor.position = new Vector3(position.x, position.y + 0.25f, position.z + airflowSpawnDistance);

            float forward01 = Mathf.Clamp01(velocity.z / Mathf.Max(0.01f, referenceForwardSpeed));
            airflowEmission.rateOverTimeMultiplier = airflowMaxRate * intensity * forward01;
        }

        // ---------------------------------------------------------------- construction

        private void ResolveWingTips()
        {
            if (leftWingTip == null) leftWingTip = FindDeep(transform, leftWingTipName);
            if (rightWingTip == null) rightWingTip = FindDeep(transform, rightWingTipName);

            if (leftWingTip != null && rightWingTip != null) return;

            // Les os attendus sont absents (modèle remplacé, os renommés) : on retombe sur deux
            // repères posés aux extrémités du maillage. Ils ne battent pas, mais l'oiseau garde
            // ses traînées de vitesse et de virage au lieu de perdre l'effet en silence.
            var renderer = GetComponentInChildren<Renderer>();
            Bounds bounds = renderer != null ? renderer.localBounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
            float halfSpan = Mathf.Max(bounds.extents.x, bounds.extents.z);

            Debug.LogWarning($"[WingWindFX] Os de bout d'aile introuvables ('{leftWingTipName}' / " +
                             $"'{rightWingTipName}'). Repères de secours posés à ±{halfSpan:F2} du corps.");

            if (leftWingTip == null) leftWingTip = CreateProxyTip("WindFX_LeftTipProxy", -halfSpan);
            if (rightWingTip == null) rightWingTip = CreateProxyTip("WindFX_RightTipProxy", halfSpan);
        }

        private Transform CreateProxyTip(string name, float localX)
        {
            var proxy = new GameObject(name).transform;
            proxy.SetParent(transform, false);
            proxy.localPosition = new Vector3(localX, 0f, 0f);
            return proxy;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private WingFX BuildWing(string name, Transform tip, float side)
        {
            if (tip == null) return null;

            // Les effets vivent à la racine de la scène plutôt que sous l'oiseau : celui-ci porte
            // une échelle de 10, qui multiplierait sans prévenir largeurs de ruban et tailles de
            // particules. Ancrés en monde, les réglages de l'inspecteur sont des unités réelles.
            var anchor = new GameObject(name).transform;
            anchor.position = tip.position;

            var wing = new WingFX
            {
                tip = tip,
                side = side,
                anchor = anchor
            };

            if (enableWingtipVortices)
            {
                wing.trail = BuildTrail(anchor.gameObject);
            }
            if (enableFlapPuffs)
            {
                wing.puff = BuildPuff(anchor);
                wing.puffEmission = wing.puff.emission;
            }

            return wing;
        }

        private TrailRenderer BuildTrail(GameObject host)
        {
            var trail = host.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.minVertexDistance = 0.035f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = BuildTaperCurve();
            trail.numCapVertices = 3;
            trail.numCornerVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.autodestruct = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            trail.sharedMaterial = ParticleBurst.ResolveMaterial("M_WindTrail");
            trail.startColor = trailColor;
            trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            // Muet tant que LateUpdate n'a pas posé l'ancre sur l'os : évite un ruban tiré depuis
            // la pose de repos du modèle jusqu'au bout d'aile à la première frame.
            trail.emitting = false;
            return trail;
        }

        /// <summary>Ruban large à la naissance puis effilé, comme un tourbillon qui se dissipe.</summary>
        private static AnimationCurve BuildTaperCurve()
        {
            var curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(0.35f, 0.7f);
            curve.AddKey(1f, 0f);
            return curve;
        }

        private ParticleSystem BuildPuff(Transform parent)
        {
            var host = new GameObject("Puffs");
            host.transform.SetParent(parent, false);

            var ps = host.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(trailColor);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildFadeGradient());

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var growth = new AnimationCurve();
            growth.AddKey(0f, 0.4f);
            growth.AddKey(0.3f, 1f);
            growth.AddKey(1f, 1.35f);
            size.size = new ParticleSystem.MinMaxCurve(1f, growth);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleBurst.ResolveMaterial("M_WindWisp");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;

            ps.Play();
            return ps;
        }

        private void BuildAirflow()
        {
            var host = new GameObject("WindFX_Airflow");
            airflowAnchor = host.transform;
            airflowAnchor.position = transform.position + Vector3.forward * airflowSpawnDistance;

            airflow = host.AddComponent<ParticleSystem>();

            var main = airflow.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            // La vitesse est donnée par le module Velocity ci-dessous et non ici : la forme
            // d'émission est un cercle, dont la direction native pousse les particules vers
            // l'extérieur du disque au lieu de les envoyer vers la caméra.
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(airflowColor);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;

            var emission = airflow.emission;
            emission.rateOverTime = 0f;
            airflowEmission = emission;

            // Cercle creux : les filets naissent autour du couloir de vol, jamais en plein écran.
            var shape = airflow.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = airflowRadius;
            shape.radiusThickness = 0.45f;
            shape.arc = 360f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            // Les filets remontent vers la caméra pendant que l'oiseau fonce dedans : la vitesse
            // relative perçue est la somme des deux, sans avoir à faire suivre les particules.
            var velocity = airflow.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.z = new ParticleSystem.MinMaxCurve(-5.5f, -3.5f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var color = airflow.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildFadeGradient());

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.35f;
            renderer.lengthScale = 2.5f;
            renderer.sharedMaterial = ParticleBurst.ResolveMaterial("M_WindTrail");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            airflow.Play();
        }

        /// <summary>Apparition brève puis disparition lente : un souffle ne claque pas, il s'étire.</summary>
        private static Gradient BuildFadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0.55f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
