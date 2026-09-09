# Entrée Kinect pour KiBird — intégration Unity

Ce dossier reçoit les commandes de vol envoyées par le bridge Python (Kinect) et les expose
exactement comme un input clavier. **Rien à installer** : pas de package, pas de dépendance,
pas de scène à préparer.

## Intégration : 4 lignes dans `MoveBird.cs`

```csharp
[Header("Kinect")]
public KinectInputSource kinectSource;   // laisser vide = clavier, comme aujourd'hui

private Vector3 GetInput()
{
    if (kinectSource != null && kinectSource.HasPlayer)
        return kinectSource.Input;

    // ... tout le code clavier/gamepad existant reste ici, inchangé ...
}
```

C'est tout. `kinectSource.Input` renvoie un `Vector3` au **même format que ton input clavier** :

| Composante | Signification | Équivalent clavier actuel |
|---|---|---|
| `x` (-1..1) | gauche / droite | A/D — `horizontalSpeed` |
| `y` (-1..1) | monter / descendre | Espace/Ctrl — `verticalSpeed` |
| `z` (-1..1) | accélérer / ralentir | W/S — modulateur de `forwardSpeed` |

Le `Bank()` et le reste de ta logique fonctionnent sans modification.

## Le clavier continue de marcher

`HasPlayer` est `false` si le bridge Python est coupé, si personne n'est devant la Kinect, ou
si le joueur sort de la zone 1-4 m. Dans tous ces cas, le code clavier reprend la main tout
seul. C'est aussi le mode debug/animateur : pas besoin de la Kinect pour développer.

## Mise en place dans la scène

1. Crée un GameObject vide, nomme-le `KinectInput`.
2. Ajoute-lui le composant **KinectInputSource** (port 7777 par défaut).
3. Glisse ce GameObject dans le champ `Kinect Source` de ton `MoveBird`.
4. Optionnel pendant la mise au point : ajoute aussi **KinectDebugOverlay** sur le même
   GameObject (F1 pour l'afficher/masquer). À désactiver pour la JPO.

## Tester sans Kinect

Le bridge sait rejouer une session enregistrée : les paquets sont identiques à ceux du mode
live, donc l'intégration se teste entièrement sans matériel.

```bash
cd tools/kinect_bridge
./run_bridge.sh --replay une_session.kbr
```

Puis Play dans Unity. L'overlay doit afficher `JOUEUR DÉTECTÉ` et les jauges doivent bouger.

## Ce que l'overlay doit montrer

- **BRIDGE HORS LIGNE** (rouge) → le script Python ne tourne pas, ou pas sur le bon port.
- **en attente d'un joueur** (orange) → le bridge émet, mais personne n'est détecté.
- **JOUEUR DÉTECTÉ** (vert) → tout va bien ; la latence affichée doit rester sous 150 ms.

## Détails d'implémentation utiles à connaître

- La socket est lue sur un **thread d'arrière-plan** qui n'appelle aucune API Unity. Les
  données sont recopiées dans `Update()`, sur le thread principal. Ne pas déplacer cette
  lecture ailleurs.
- Seul le **paquet le plus récent** est conservé : si Unity descend sous 30 FPS, aucune file
  ne s'accumule et la latence ne dérive pas.
- Un redémarrage du bridge Python (son compteur de séquence repart à zéro) est détecté et
  géré : Unity ne se bloque pas.
- Le format binaire (187 octets) est décrit dans `implementation_plan.md` §2. La source de
  vérité exécutable est `tools/kinect_bridge/kibird_bridge/protocol.py`, et
  `tools/kinect_bridge/tests/test_unity_contract.py` vérifie que les offsets du C# et du
  Python restent alignés. **Si tu changes le format d'un côté, ce test te dira quoi corriger
  de l'autre.**
