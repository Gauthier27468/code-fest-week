using UnityEngine;

namespace KiBird.FX
{
    /// <summary>
    /// Éclats de particules générés à la volée, sans préfab à préparer : plumes à la mort de
    /// l'oiseau, étincelles au franchissement d'un anneau, confettis à la victoire.
    /// Les trois effets partagent la même construction, seuls leurs réglages diffèrent.
    /// </summary>
    public static class ParticleBurst
    {
        public struct Settings
        {
            public string name;
            public Vector2 lifetime;
            public Vector2 speed;
            public Vector2 size;
            public int minCount;
            public int maxCount;
            public float radius;
            public float gravity;
            public float duration;
            public float destroyAfter;
            public Color color;
            /// <summary>Dégradé appliqué sur la durée de vie. Null = couleur fixe.</summary>
            public Gradient gradient;
            /// <summary>Rotation aléatoire au tir puis en continu (plumes qui virevoltent).</summary>
            public bool spin;
            /// <summary>Apparition/disparition progressive de la taille. Null = taille constante.</summary>
            public AnimationCurve sizeOverLifetime;
            /// <summary>Matériau chargé depuis un dossier Resources. Vide = particule blanche.</summary>
            public string materialResource;
        }

        /// <summary>Plumes projetées à l'impact mortel.</summary>
        public static Settings Feathers => new Settings
        {
            name = "FeatherExplosion",
            lifetime = new Vector2(1.8f, 3.2f),
            speed = new Vector2(2.5f, 6.5f),
            size = new Vector2(0.35f, 0.75f),
            minCount = 35,
            maxCount = 55,
            radius = 0.5f,
            gravity = 0.12f,
            duration = 1.0f,
            destroyAfter = 4.0f,
            gradient = BuildGradient(
                new[] { Color.white, new Color(0.9f, 0.95f, 1f), new Color(0.75f, 0.85f, 0.95f) },
                new[] { 0f, 0.5f, 1f },
                new[] { 1f, 0.85f, 0f },
                new[] { 0f, 0.65f, 1f }),
            spin = true,
            sizeOverLifetime = BuildCurve((0f, 0.6f), (0.15f, 1f), (0.75f, 1f), (1f, 0f)),
            materialResource = "M_Feather"
        };

        /// <summary>Éclats dorés au franchissement d'un anneau.</summary>
        public static Settings HoopSparkles => new Settings
        {
            name = "HoopCollectFX",
            lifetime = new Vector2(0.4f, 0.7f),
            speed = new Vector2(3f, 6.5f),
            size = new Vector2(0.12f, 0.3f),
            minCount = 20,
            maxCount = 35,
            radius = 0.7f,
            gravity = 0f,
            duration = 0.4f,
            destroyAfter = 1.0f,
            color = new Color(1f, 0.85f, 0.2f, 1f)
        };

        /// <summary>Confettis à l'arrivée dans le nid.</summary>
        public static Settings VictoryConfetti => new Settings
        {
            name = "VictoryConfetti",
            lifetime = new Vector2(1.5f, 2.5f),
            speed = new Vector2(3f, 8f),
            size = new Vector2(0.15f, 0.35f),
            minCount = 50,
            maxCount = 80,
            radius = 1.0f,
            gravity = 0.3f,
            duration = 1.0f,
            destroyAfter = 3.0f,
            gradient = BuildGradient(
                new[] { new Color(0.2f, 0.95f, 0.4f), new Color(1f, 0.85f, 0.2f), new Color(0.2f, 0.8f, 1f) },
                new[] { 0f, 0.5f, 1f },
                new[] { 1f, 0.8f, 0f },
                new[] { 0f, 0.7f, 1f })
        };

        /// <summary>Instancie le préfab s'il y en a un, sinon construit l'effet décrit par settings.</summary>
        public static void Play(Vector3 position, Settings settings, GameObject overridePrefab = null)
        {
            if (overridePrefab != null)
            {
                GameObject instance = Object.Instantiate(overridePrefab, position, Quaternion.identity);
                Object.Destroy(instance, settings.destroyAfter);
                return;
            }

            Emit(position, settings);
        }

        private static void Emit(Vector3 position, Settings s)
        {
            var fxObj = new GameObject(s.name);
            fxObj.transform.position = position;

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            Material material = ResolveMaterial(s.materialResource);
            if (material != null)
            {
                fxObj.GetComponent<ParticleSystemRenderer>().material = material;
            }

            var main = ps.main;
            main.duration = s.duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifetime.x, s.lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
            main.gravityModifier = s.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            if (s.gradient == null) main.startColor = s.color;
            if (s.spin) main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)s.minCount, (short)s.maxCount)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = s.radius;

            if (s.gradient != null)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = s.gradient;
            }

            if (s.spin)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-150f * Mathf.Deg2Rad, 150f * Mathf.Deg2Rad);
            }

            if (s.sizeOverLifetime != null)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, s.sizeOverLifetime);
            }

            ps.Play();
            Object.Destroy(fxObj, s.destroyAfter);
        }

        /// <summary>
        /// Matériau du dossier Resources, sinon un shader de particules non éclairé. Passer par
        /// Resources et non AssetDatabase : ce dernier n'existe pas dans un build.
        /// </summary>
        private static Material ResolveMaterial(string resourceName)
        {
            if (!string.IsNullOrEmpty(resourceName))
            {
                Material fromResources = Resources.Load<Material>(resourceName);
                if (fromResources != null) return fromResources;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            return shader != null ? new Material(shader) : null;
        }

        private static Gradient BuildGradient(Color[] colors, float[] colorTimes,
            float[] alphas, float[] alphaTimes)
        {
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(colors[i], colorTimes[i]);
            }

            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                alphaKeys[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        private static AnimationCurve BuildCurve(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach ((float time, float value) in keys)
            {
                curve.AddKey(time, value);
            }
            return curve;
        }
    }
}
