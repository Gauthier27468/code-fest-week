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
            /// <summary>Dégradé de couleur sur la durée de vie (ou palette, si randomColor).</summary>
            public Gradient gradient;
            /// <summary>Couleur aléatoire tirée du dégradé par particule (confettis multicolores).</summary>
            public bool randomColor;
            /// <summary>Rotation aléatoire au tir puis en continu (plumes qui virevoltent, confettis).</summary>
            public bool spin;
            /// <summary>Particules rectangulaires 3D (format confetti).</summary>
            public bool size3D;
            /// <summary>Apparition/disparition progressive de la taille. Null = taille constante.</summary>
            public AnimationCurve sizeOverLifetime;
            /// <summary>Matériau chargé depuis un dossier Resources ou AssetDatabase. Vide = particule blanche.</summary>
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

        /// <summary>Confettis multicolores au franchissement d'un anneau.</summary>
        public static Settings HoopConfetti => new Settings
        {
            name = "HoopCollectFX",
            lifetime = new Vector2(0.9f, 1.6f),
            speed = new Vector2(4f, 8.5f),
            size = new Vector2(0.12f, 0.24f),
            minCount = 45,
            maxCount = 70,
            radius = 0.8f,
            gravity = 0.35f,
            duration = 0.5f,
            destroyAfter = 2.5f,
            gradient = BuildRainbowGradient(),
            randomColor = true,
            spin = true,
            size3D = true,
            sizeOverLifetime = BuildCurve((0f, 0.85f), (0.15f, 1f), (0.75f, 1f), (1f, 0.15f)),
            materialResource = "M_Confetti"
        };

        /// <summary>Confettis à l'arrivée dans le nid.</summary>
        public static Settings VictoryConfetti => new Settings
        {
            name = "VictoryConfetti",
            lifetime = new Vector2(1.5f, 2.5f),
            speed = new Vector2(3f, 8f),
            size = new Vector2(0.12f, 0.25f),
            minCount = 60,
            maxCount = 90,
            radius = 1.0f,
            gravity = 0.3f,
            duration = 1.0f,
            destroyAfter = 3.0f,
            gradient = BuildRainbowGradient(),
            randomColor = true,
            spin = true,
            size3D = true,
            sizeOverLifetime = BuildCurve((0f, 0.85f), (0.15f, 1f), (0.75f, 1f), (1f, 0.15f)),
            materialResource = "M_Confetti"
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
            // Empêche l'erreur "Setting the duration while system is still playing is not supported"
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Material material = ResolveMaterial(s.materialResource);
            var psr = fxObj.GetComponent<ParticleSystemRenderer>();
            if (material != null)
            {
                psr.material = material;
            }
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sortMode = ParticleSystemSortMode.Distance;

            var main = ps.main;
            main.duration = s.duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifetime.x, s.lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
            if (s.size3D)
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
                main.startSizeY = new ParticleSystem.MinMaxCurve(s.size.x * 2.2f, s.size.y * 2.2f);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 1f);
            }
            else
            {
                main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
            }
            main.gravityModifier = s.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            if (s.randomColor)
            {
                // Chaque particule tire sa couleur dans le dégradé, puis s'estompe en fin de vie.
                main.startColor = new ParticleSystem.MinMaxGradient(s.gradient)
                {
                    mode = ParticleSystemGradientMode.RandomColor
                };
                colorOverLifetime.color = BuildGradient(
                    new[] { Color.white, Color.white }, new[] { 0f, 1f },
                    new[] { 1f, 1f, 0f }, new[] { 0f, 0.75f, 1f });
            }
            else
            {
                colorOverLifetime.color = s.gradient;
            }

            if (s.spin)
            {
                if (s.size3D)
                {
                    main.startRotation3D = true;
                    main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                    main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                    main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                }
                else
                {
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                }
            }

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)s.minCount, (short)s.maxCount)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = s.radius;

            if (s.spin)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                if (s.size3D)
                {
                    rot.separateAxes = true;
                    rot.x = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);
                    rot.y = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);
                    rot.z = new ParticleSystem.MinMaxCurve(-240f * Mathf.Deg2Rad, 240f * Mathf.Deg2Rad);
                }
                else
                {
                    rot.z = new ParticleSystem.MinMaxCurve(-150f * Mathf.Deg2Rad, 150f * Mathf.Deg2Rad);
                }
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
        /// Matériau du dossier Resources, sinon du dossier Assets/Art/Generated en Éditeur,
        /// sinon un shader de particules non éclairé double-face.
        /// </summary>
        public static Material ResolveMaterial(string resourceName)
        {
            if (!string.IsNullOrEmpty(resourceName))
            {
                Material fromResources = Resources.Load<Material>(resourceName);
                if (fromResources != null) return fromResources;

#if UNITY_EDITOR
                Material fromAssetDb = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Generated/{resourceName}.mat")
                                    ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Generated/Resources/{resourceName}.mat");
                if (fromAssetDb != null) return fromAssetDb;
#endif
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.SetInt("_Cull", 0); // Visible des deux côtés pour confettis

            // Le shader URP naît opaque et sans texture : tel quel, un effet privé de son matériau
            // Resources s'afficherait en quads blancs pleins — des « cubes » dans le ciel — au lieu
            // de disparaître discrètement. On force donc le mélange alpha sur ce filet de secours.
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
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

        private static Gradient BuildRainbowGradient()
        {
            return BuildGradient(
                new[]
                {
                    new Color(1.0f, 0.15f, 0.22f), // Rouge vif
                    new Color(1.0f, 0.52f, 0.05f), // Orange éclatant
                    new Color(1.0f, 0.88f, 0.10f), // Jaune / Or
                    new Color(0.12f, 0.92f, 0.36f), // Vert émeraude
                    new Color(0.05f, 0.82f, 1.00f), // Cyan vibrant
                    new Color(0.28f, 0.45f, 1.00f), // Bleu royal
                    new Color(0.96f, 0.18f, 0.82f)  // Rose magenta
                },
                new[] { 0.00f, 0.16f, 0.33f, 0.50f, 0.67f, 0.83f, 1.00f },
                new[] { 1.0f, 1.0f },
                new[] { 0.0f, 1.0f }
            );
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
