# Bridge Kinect → Unity (KiBird)

Capture Kinect 1414 → détection de pose → verrouillage joueur → traduction en gestes de vol
→ émission UDP binaire vers Unity.

## Démarrage rapide

```bash
./run_bridge.sh              # mode live (Kinect branchée)
./run_bridge.sh --mirror     # si le ressenti gauche/droite est inversé à l'installation
```

Sans Kinect, le jeu reste entièrement pilotable au clavier : voir
`Assets/Scripts/KinectInput/README.md`.

`run_bridge.sh` fait un `uv sync` (venv + dépendances depuis `pyproject.toml`) et télécharge le
modèle MediaPipe au premier lancement. Pour préparer l'environnement sans lancer le bridge :

```bash
uv sync
```

### Dépendances système requises

`uv sync` suffit pour le Python, mais **la bibliothèque C `libfreenect` doit être installée sur
la machine**, avec ses headers de développement :

```bash
sudo pacman -S libfreenect        # Arch — fournit /usr/lib/libfreenect.so.0 ET /usr/include/libfreenect/
```

Le paquet PyPI `freenect` (déclaré dans `pyproject.toml`) n'est qu'un binding Cython : il se
compile au `uv sync` contre ces headers, puis se lie à `/usr/lib/libfreenect.so.0` à l'exécution.
Il **ne dispense donc pas** d'installer libfreenect — il évite seulement de dépendre du binding
Python fourni par le paquet système, ce qui permet au venv d'être autonome (plus de
`--system-site-packages`) et laisse uv choisir librement l'interpréteur.

**Device USB parfois bancal à l'ouverture** : juste après l'ouverture du device, `freenect`
renvoie parfois `None` sur la toute première image (ce qui donnait l'impression qu'il fallait
relancer le script 2-3 fois à la main pour que ça marche). Le bridge referme et rouvre
maintenant l'accès Kinect automatiquement jusqu'à 4 fois (1,5 s entre chaque essai) avant
d'abandonner — un seul lancement suffit dans l'immense majorité des cas.

## ✅ Blocage mediapipe 1.0.1 (résolu)

**Symptôme** : `PoseLandmarker.create_from_options()` tuait le process entier par `SIGKILL`
(exit 137), de façon 100 % reproductible, avant même de traiter la moindre frame.

**Cause** : c'est une régression de **mediapipe 1.0.1**, qui a introduit une nouvelle
architecture où toute la logique native vit dans `libmediapipe.so` (114 Mo) chargée via
`ctypes`. `LD_DEBUG=libs` montre que le crash survient pendant `calling init:
libmediapipe.so`, c'est-à-dire dans les **constructeurs statiques C++** de la bibliothèque —
avant tout appel d'API. `strace` confirme un `SIGKILL` reçu par les ~85 threads du process
simultanément (en 70 µs), sans qu'aucun `kill`/`tgkill` ne soit émis par le process lui-même.

**Correctif** : rester en **mediapipe 0.10.35** (architecture pybind11, éprouvée), figée dans
`pyproject.toml`. Fonctionne sur Python 3.12 **et** 3.14.

**Ce que le correctif a permis de vérifier** :
- Inférence mesurée à **8,5 ms/frame (≈118 FPS)** sur ce CPU — très au-dessus des 30 Hz visés,
  large marge sous le budget de latence de 150 ms.
- Contexte GL créé sans problème sur l'iGPU AMD Radeon 780M, delegate XNNPACK initialisé.

**Fausses pistes écartées en chemin** (ne pas les reprendre) : ce n'était ni l'OOM killer
(`/proc/vmstat` `oom_kill` resté à 0 sur trois cgroups différents), ni une limite cgroup, ni
`systemd-oomd`/`earlyoom`, ni seccomp/AppArmor, ni la version de Python (3.12 et 3.14 plantaient
identiquement), ni EGL/le GPU NVIDIA Optimus (forcer le vendor Mesa ne changeait rien), ni
AVX512 sur le Ryzen 7 7840HS — 0.10.35 fait tourner XNNPACK sur exactement le même CPU.

## ⚠️ Piège matériel : le module noyau `gspca_kinect`

Le noyau charge un pilote `gspca_kinect` qui revendique la caméra RGB de la Kinect et entre en
conflit avec libfreenect (`Error: Can't open device`). Si le bridge ne trouve pas la Kinect
alors qu'elle est bien branchée (`lsusb | grep -i xbox` la montre) :

```bash
sudo rmmod gspca_kinect                              # décharge à chaud
echo "blacklist gspca_kinect" | sudo tee /etc/modprobe.d/kibird-kinect.conf   # définitif
```

À faire avant la JPO sur la machine de démo, sinon un simple redémarrage peut faire réapparaître
le problème au pire moment.

## Structure

```
kibird_bridge/
  protocol.py   # contrat réseau UDP (187 octets, source de vérité)
  capture.py    # Kinect (freenect) : RGB + profondeur alignée
  tracking.py   # zone 1-4m, verrouillage joueur, délai de grâce 2.5s — 6 tests
  gestures.py   # lean/lift/glide/throttle + filtre One Euro — 13 tests
  segmentation.py # suppression du fond au-delà de 4 m via la profondeur IR — 6 tests
### Retour caméra avec overlay (`--preview`)

Ouvre une fenêtre OpenCV montrant l'image de la Kinect avec, superposés : tous les squelettes
détectés par MediaPipe (le joueur verrouillé en vert épais, les détections ignorées en gris
fin), la taille des points proportionnelle à la confiance du landmark, et un HUD avec la
cadence, la distance, l'état zone/calibration et les 4 commandes envoyées à Unity.

```bash
./run_bridge.sh --preview                     # fenêtre plein format
./run_bridge.sh --preview --preview-scale 0.5 # demi-taille, pour laisser la place au jeu
```

Touches dans la fenêtre : `q` / `Échap` arrêtent le bridge, `f` bascule entre l'image brute de
la caméra et l'image réellement envoyée à MediaPipe (utile pour régler `--bg-max-distance`).

Prérequis : un OpenCV **avec** HighGUI, absent du wheel `opencv-python-headless` installé par
défaut. Il est déclaré dans l'extra `preview` :

```bash
uv sync --extra preview
```

Option de debug/réglage : `imshow` coûte quelques millisecondes par frame et la fenêtre
s'affiche par-dessus le jeu — à ne pas activer pendant une JPO.

### Validation matériel
  fixtures/     # image de test réelle utilisée par le test d'intégration
models/         # modèle .task téléchargé par run_bridge.sh (non versionné, cf. .gitignore)
bridge_entry.py     # point d'entrée du binaire gelé (PyInstaller veut un script, pas un module)
kibird_bridge.spec  # recette PyInstaller (mediapipe, freenect, modèle .task)
build_bridge.sh     # gèle le bridge et le copie où on lui demande (appelé par Unity au build)
dist/, build/       # artefacts PyInstaller, non versionnés
```

### Filtre de fond (profondeur IR)

Avant d'envoyer l'image à MediaPipe, tous les pixels situés au-delà de **4 m** (limite de la
zone de jeu) sont noircis à partir de la profondeur IR alignée sur la RGB. Les visiteurs qui
passent derrière le joueur ne produisent donc plus de squelette du tout. Coût mesuré :
~4 ms/frame (budget 33 ms à 30 Hz).

```bash
./run_bridge.sh --bg-max-distance 3.5   # resserrer la coupure
./run_bridge.sh --no-bg-filter          # désactiver (debug)
```

Garde-fous : les trous IR sur le joueur sont bouchés (fermeture morphologique) et une depth
inexploitable laisse l'image intacte plutôt que de la noircir entièrement.

### Retour caméra avec overlay (`--preview`)

Ouvre une fenêtre OpenCV montrant l'image de la Kinect avec, superposés : tous les squelettes
détectés par MediaPipe (le joueur verrouillé en vert épais, les détections ignorées en gris
fin), la taille des points proportionnelle à la confiance du landmark, et un HUD avec la
cadence, la distance, l'état zone/calibration et les 4 commandes envoyées à Unity.

```bash
./run_bridge.sh --preview                     # fenêtre plein format
./run_bridge.sh --preview --preview-scale 0.5 # demi-taille, pour laisser la place au jeu
```

Touches dans la fenêtre : `q` / `Échap` arrêtent le bridge, `f` bascule entre l'image brute de
la caméra et l'image réellement envoyée à MediaPipe (utile pour régler `--bg-max-distance`).

Prérequis : un OpenCV **avec** HighGUI, absent du wheel `opencv-python-headless` installé par
défaut. Il est déclaré dans l'extra `preview` :

```bash
uv sync --extra preview
```

Option de debug/réglage : `imshow` coûte quelques millisecondes par frame et la fenêtre
s'affiche par-dessus le jeu — à ne pas activer pendant une JPO.

### Validation matériel

La boucle live complète (Kinect → pose → gestes → UDP) a tourné bout en bout depuis le binaire
gelé : inférence à **29,8 Hz**, joueur réel suivi entre 1,98 m et 3,22 m, **11,5 ms** de latence
— très en dessous du budget de 150 ms.

À vérifier à l'installation, overlay **F1** ouvert dans Unity : la distance affichée doit
correspondre au mètre ruban (1-4 m), et le sens gauche/droite doit être naturel — si l'oiseau
part du mauvais côté, relancer avec `--mirror`.

## Intégration Unity

Rien à câbler dans la scène. Au lancement du jeu, `KinectBridgeLauncher` démarre lui-même le
bridge (et le tue proprement à la fermeture, ou le relance automatiquement — jusqu'à 5 fois —
s'il s'arrête en cours de partie) : plus besoin d'ouvrir un terminal côté animateur.

Il essaie deux sources, dans cet ordre :

1. `<Build>/<Produit>_Data/StreamingAssets/kinect_bridge/kibird_bridge` — le **binaire gelé**,
   déposé automatiquement à chaque build (voir ci-dessous). C'est le cas normal sur la machine
   de démo ;
2. `<dossier du projet>/tools/kinect_bridge/run_bridge.sh` — le code source, pour le poste de
   dev et l'éditeur.

Si aucune des deux n'existe, le lancement auto est simplement ignoré et le clavier reste
disponible. Lancer le bridge à la main dans un terminal fonctionne toujours en plus, pour du
debug.

### Build : un seul dossier à copier

`Assets/Editor/KinectBridgeBuildStep.cs` se déclenche après chaque build Linux : il appelle
`build_bridge.sh`, qui gèle le bridge avec PyInstaller et copie le résultat dans les
StreamingAssets du build. **Plus rien à copier à côté de l'exécutable**, et ni Python ni uv ne
sont nécessaires sur la machine de démo.

```bash
./build_bridge.sh              # à la main : produit dist/kibird_bridge/ (~365 Mo, ~20 s)
KIBIRD_SKIP_BRIDGE_BUILD=1     # variable d'env : saute l'étape pour un build de test rapide
```

Choix de packaging : `--onedir` et non `--onefile`, car le onefile réextrait les ~365 Mo dans
`/tmp` à chaque démarrage (2-5 s de latence), incompatible avec la relance automatique du
launcher pendant la JPO.

⚠️ **Le binaire produit n'est pas portable d'une distribution à l'autre** : il embarque
`libfreenect.so.0` et reste lié à la glibc de la machine qui l'a compilé. Builder sur une
machine équivalente à celle de la démo (ou sur la machine de démo elle-même).

`KinectInputSource` se crée tout seul de son côté au lancement (`RuntimeInitializeOnLoadMethod`,
avec `KinectDebugOverlay` masqué) et `MoveBird` l'interroge via `KinectInputSource.Instance` :

- un joueur est verrouillé et les paquets sont frais → la Kinect pilote l'oiseau ;
- sinon (bridge éteint, personne dans la zone, perte de suivi) → le clavier reprend la main,
  sans transition ni message d'erreur.

Le mapping est direct, aucune logique de geste côté Unity :
`lean → x` (gauche/droite), `lift → y` (altitude), `throttle → z` (vitesse, ±40 % de
`forwardSpeed` tant que `autoMoveForward` est coché).

Pour poser l'écouteur à la main quand même (afin d'en régler le port ou les timeouts dans
l'inspecteur), ajouter un GameObject avec `KinectInputSource` : celui de la scène gagne, le
bootstrap automatique ne crée alors rien. Décocher `useKinectWhenAvailable` sur `MoveBird`
force le clavier.

**F1** affiche/masque l'overlay de debug (état du bridge, distance, latence, jauges, squelette).

## Tester sans Kinect

Le jeu est entièrement jouable au clavier quand aucun joueur n'est détecté : Espace tenu 3 s
pour démarrer, puis A/Q/D pour tourner, Espace pour battre des ailes, Ctrl/C pour piquer,
W/Z et S pour la vitesse, Maj pour accélérer, K pour mourir. Voir
`Assets/Scripts/KinectInput/README.md`.

## Format réseau

UDP `127.0.0.1:7777`, little-endian, 187 octets/paquet, 30 Hz constants, heartbeat
même sans joueur (`player_present=false`). `kibird_bridge/protocol.py` est la source de
vérité exécutable — s'y référer en cas de divergence avec le plan.
