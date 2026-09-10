using UnityEngine;

/// <summary>
/// Explosion festive de confettis multicolores, réutilisable partout dans le jeu
/// (franchissement d'anneau, nouveau record...). Entièrement procédurale : aucun prefab requis.
/// </summary>
public static class ConfettiEffect
{
    public static void SpawnBurst(Vector3 worldPosition)
    {
        GameObject fxObj = new GameObject("ConfettiFX");
        fxObj.transform.position = worldPosition;

        ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();

        // Matériau pour les confettis (avec texture rounded_rect et shader unlit double-face)
        Material confettiMat = null;
#if UNITY_EDITOR
        confettiMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Generated/M_Confetti.mat");
#endif
        if (confettiMat == null)
        {
            Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                          ?? Shader.Find("Particles/Standard Unlit")
                          ?? Shader.Find("Sprites/Default");
            if (pShader != null)
            {
                confettiMat = new Material(pShader);
                confettiMat.SetInt("_Cull", 0); // Visible des deux côtés lors des cabrioles 3D
            }
        }
        if (confettiMat != null)
        {
            psr.material = confettiMat;
        }

        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sortMode = ParticleSystemSortMode.Distance;

        // Palette arc-en-ciel complète pour un effet "toutes les couleurs" (Rouge, Orange, Jaune, Vert, Cyan, Bleu, Magenta)
        Gradient rainbowGrad = new Gradient();
        rainbowGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1.0f, 0.15f, 0.22f), 0.00f), // Rouge vif
                new GradientColorKey(new Color(1.0f, 0.52f, 0.05f), 0.16f), // Orange éclatant
                new GradientColorKey(new Color(1.0f, 0.88f, 0.10f), 0.33f), // Jaune / Or
                new GradientColorKey(new Color(0.12f, 0.92f, 0.36f), 0.50f), // Vert émeraude
                new GradientColorKey(new Color(0.05f, 0.82f, 1.00f), 0.67f), // Cyan vibrant
                new GradientColorKey(new Color(0.28f, 0.45f, 1.00f), 0.83f), // Bleu royal
                new GradientColorKey(new Color(0.96f, 0.18f, 0.82f), 1.00f)  // Rose magenta
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f)
            }
        );

        // Paramètres principaux du ParticleSystem
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 9.0f);

        // Forme rectangulaire caractéristique des lamelles de confettis
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.24f, 0.50f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(1.0f, 1.0f);

        // Orientation 3D initiale aléatoire
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

        // Sélection aléatoire d'une couleur dans la palette pour chaque confetto
        var minMaxGrad = new ParticleSystem.MinMaxGradient(rainbowGrad);
        minMaxGrad.mode = ParticleSystemGradientMode.RandomColor;
        main.startColor = minMaxGrad;

        // Gravité douce pour faire flotter et retomber les confettis
        main.gravityModifier = 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // Salve explosive festive (65 à 95 confettis)
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0.0f, (short)65, (short)95)
        });

        // Zone d'explosion sphérique autour du point d'origine
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.8f;

        // Décélération aérodynamique (l'explosion ralentit vite pour laisser planer les confettis)
        var limitVelocity = ps.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.limit = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
        limitVelocity.dampen = 0.32f;

        // Rotation & vrille 3D continue dans l'air
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);
        rot.y = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);
        rot.z = new ParticleSystem.MinMaxCurve(-240f * Mathf.Deg2Rad, 240f * Mathf.Deg2Rad);

        // Turbulence d'air / flottement ondulé
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.3f;
        noise.damping = true;

        // Fondu transparent fluide en fin de parcours
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient fadeGrad = new Gradient();
        fadeGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.70f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        col.color = fadeGrad;

        // Rétrécissement progressif en fin de vie
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0.0f, 0.85f);
        sizeCurve.AddKey(0.12f, 1.0f);
        sizeCurve.AddKey(0.75f, 1.0f);
        sizeCurve.AddKey(1.0f, 0.15f);
        sol.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

        ps.Play();
        Object.Destroy(fxObj, 2.5f);
    }
}
