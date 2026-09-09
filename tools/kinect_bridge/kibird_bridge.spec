# -*- mode: python ; coding: utf-8 -*-
"""Spec PyInstaller du bridge KiBird : produit dist/kibird_bridge/, autonome, à déposer dans
les StreamingAssets du build Unity (voir Assets/Editor/KinectBridgeBuildStep.cs).

Mode --onedir volontairement, PAS --onefile : le onefile réextrait ~400 Mo dans /tmp à chaque
démarrage (2-5 s de latence), rédhibitoire avec la relance automatique du launcher pendant la
JPO. Le onedir démarre immédiatement.
"""
from PyInstaller.utils.hooks import collect_all

# mediapipe charge à l'exécution quantité de fichiers qui ne sont pas des imports Python
# (graphes .binarypb, modèles .tflite, bibliothèques natives) : seul collect_all les embarque.
mp_datas, mp_binaries, mp_hiddenimports = collect_all("mediapipe")

a = Analysis(
    ["bridge_entry.py"],
    pathex=["."],
    binaries=mp_binaries,
    # Le modèle PoseLandmarker voyage dans le bundle ; bridge.py le retrouve via sys._MEIPASS
    # (cf. _default_model_path).
    datas=mp_datas + [("models/pose_landmarker_lite.task", "models")],
    # `freenect` est un module d'extension C (.so) sans import statique repérable depuis
    # capture.py, qui l'importe paresseusement : sans ce hiddenimport il serait absent du bundle.
    # PyInstaller suit ensuite ses dépendances natives (libfreenect.so.0, libfreenect_sync.so.0)
    # tout seul.
    hiddenimports=mp_hiddenimports + ["freenect"],
    hookspath=[],
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    exclude_binaries=True,
    name="kibird_bridge",
    debug=False,
    strip=False,
    upx=False,
    console=True,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    name="kibird_bridge",
)
