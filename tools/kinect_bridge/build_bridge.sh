#!/usr/bin/env bash
# Gèle le bridge Kinect en binaire autonome avec PyInstaller.
#
#   ./build_bridge.sh                 # produit dist/kibird_bridge/
#   ./build_bridge.sh <dossier_dest>  # ... puis le recopie dans <dossier_dest>
#
# Le second usage est celui de Unity : KinectBridgeBuildStep.cs appelle ce script après chaque
# build en lui passant .../<Produit>_Data/StreamingAssets/kinect_bridge.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

DEST="${1:-}"

uv sync --quiet

if [ ! -f "models/pose_landmarker_lite.task" ]; then
    echo "Téléchargement du modèle PoseLandmarker (lite)..."
    mkdir -p models
    curl -sL -o models/pose_landmarker_lite.task \
        "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task"
fi

echo "Compilation du bridge (PyInstaller, ~1 min)..."
uv run --quiet pyinstaller --noconfirm --clean --log-level WARN kibird_bridge.spec

if [ -n "$DEST" ]; then
    # `cp -a` et non un copieur C# côté Unity : il préserve le bit exécutable et les liens
    # symboliques de versionnage des .so (libfreenect.so.0 -> libfreenect.so.0.7.5), que
    # File.Copy aplatirait en perdant les permissions.
    echo "Copie vers $DEST ..."
    rm -rf "$DEST"
    mkdir -p "$DEST"
    cp -a dist/kibird_bridge/. "$DEST/"
fi

echo "Bridge compilé : $(du -sh dist/kibird_bridge | cut -f1)"
