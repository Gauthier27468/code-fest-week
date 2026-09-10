# Entrée Kinect — intégration Unity

La source physique est choisie dans `kibird-config.toml`, à la racine du projet en éditeur et
à côté de l'exécutable dans un build. Les modes disponibles sont `kinect`, `webcam` et `auto`.
Le bridge crée automatiquement ce fichier avec les valeurs par défaut s'il est absent.

Ce dossier reçoit les commandes de vol envoyées par le bridge Python (Kinect) en UDP et les
expose exactement comme un input clavier. Rien à installer, aucune scène à préparer :
`KinectInputSource` et `KinectBridgeLauncher` se créent tout seuls au lancement.

## Format des commandes

`KinectInputSource.Input` renvoie un `Vector3` au même format que l'input clavier de `MoveBird` :

| Composante | Signification | Équivalent clavier |
|---|---|---|
| `x` (-1..1) | gauche / droite | A/Q/D — `horizontalSpeed` |
| `y` (-1..1) | monter / descendre | Espace/Ctrl — `verticalSpeed` |
| `z` (-1..1) | accélérer / ralentir | W/Z/S — modulateur de `forwardSpeed` |

## Le clavier continue de marcher

`HasPlayer` est `false` si le bridge Python est coupé, si personne n'est devant la Kinect, ou
si le joueur sort de la zone 1-4 m. Dans tous ces cas `MoveBird` reprend les touches tout seul.
C'est aussi le mode debug/animateur : pas besoin de la Kinect pour développer.

## Overlay de contrôle (F1)

`KinectDebugOverlay` est ajouté automatiquement, masqué par défaut. F1 l'affiche :

- **BRIDGE HORS LIGNE** (rouge) → le script Python ne tourne pas, ou pas sur le bon port.
- **en attente d'un joueur** (orange) → le bridge émet, mais personne n'est détecté.
- **JOUEUR DÉTECTÉ** (vert) → tout va bien ; la latence affichée doit rester sous 150 ms.

## Détails d'implémentation

- La socket est lue sur un thread d'arrière-plan qui n'appelle aucune API Unity. Les données
  sont recopiées dans `Update()`, sur le thread principal. Ne pas déplacer cette lecture.
- Seul le paquet le plus récent est conservé : si Unity descend sous 30 FPS, aucune file ne
  s'accumule et la latence ne dérive pas.
- Un redémarrage du bridge Python (compteur de séquence remis à zéro) est détecté et géré.
- Le format binaire (187 octets) a pour source de vérité
  `tools/kinect_bridge/kibird_bridge/protocol.py`. `tools/kinect_bridge/tests/test_unity_contract.py`
  vérifie que les offsets du C# et du Python restent alignés : si tu changes le format d'un
  côté, ce test dit quoi corriger de l'autre.
