# Bridge Kinect → Unity (KiBird)

Implémente `implementation_plan.md` (racine du repo) : capture Kinect 1414 → détection de
pose → verrouillage joueur → traduction en gestes de vol → émission UDP binaire vers Unity.

## Démarrage rapide

```bash
./run_bridge.sh                          # mode live (Kinect branchée)
./run_bridge.sh --replay session.kbr     # rejoue une session enregistrée (aucun matériel requis)
python -m kibird_bridge.monitor          # observe les paquets reçus, dans un autre terminal
```

`run_bridge.sh` crée le venv (`uv venv --system-site-packages`), installe les dépendances et
télécharge le modèle MediaPipe au premier lancement.

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
`requirements.txt`. Fonctionne sur Python 3.12 **et** 3.14 — donc aucun conflit avec `freenect`,
qui n'est installé que pour le Python système (3.14).

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
  protocol.py   # contrat réseau UDP (187 octets, source de vérité — voir implementation_plan.md §2)
  capture.py    # Kinect (freenect) : RGB + profondeur alignée — validé sur la vraie Kinect
  tracking.py   # zone 1-4m, verrouillage joueur, délai de grâce 2.5s — 6 tests
  gestures.py   # lean/lift/glide/throttle + filtre One Euro — 8 tests
  recorder.py   # enregistrement/relecture de sessions .kbr — 2 tests
  pose.py       # wrapper MediaPipe PoseLandmarker — validé sur image réelle
  bridge.py     # orchestration CLI (live + --replay)
  monitor.py    # récepteur console de vérification (Plan de vérification, P1)
tests/          # 24 tests, tous passent : for t in tests/test_*.py; do .venv/bin/python $t; done
  fixtures/     # image de test réelle utilisée par le test d'intégration
models/         # modèle .task téléchargé par run_bridge.sh (non versionné, cf. .gitignore)
```

### Ce qui reste à valider sur matériel

La Kinect a été débranchée en cours de session, donc la **boucle live complète**
(Kinect → pose → gestes → UDP en continu) n'a pas encore tourné bout en bout. Chaque maillon
est validé séparément : capture Kinect réelle ✅, inférence sur image réelle ✅, gestes ✅,
protocole ✅, émission UDP ✅. À faire une fois la Kinect rebranchée :

```bash
./run_bridge.sh                    # terminal 1
python -m kibird_bridge.monitor    # terminal 2 : vérifier distance, lean, glide en direct
```

Points à surveiller en particulier : la distance affichée doit correspondre au mètre ruban
(1-4 m), et le sens gauche/droite doit être naturel — si l'oiseau part du mauvais côté,
relancer avec `--mirror`.

## Tester sans Kinect ni mediapipe (dès maintenant)

```bash
# Terminal 1 : observer les paquets
python -m kibird_bridge.monitor

# Terminal 2 : rejouer une session (ou une session factice pour un premier test)
python -m kibird_bridge.bridge --replay <fichier>.kbr
```

C'est le mode à utiliser pour intégrer `KinectInputSource.cs` côté Unity : le format des
paquets reçus est identique à celui du mode live, seul le champ `replay_mode` (flag bit 2)
change.

## Format réseau

Voir `implementation_plan.md` section 2 (racine du repo) pour la spécification complète.
Résumé : UDP `127.0.0.1:7777`, little-endian, 187 octets/paquet, 30 Hz constants, heartbeat
même sans joueur (`player_present=false`). `kibird_bridge/protocol.py` est la source de
vérité exécutable — s'y référer en cas de divergence avec le plan.
