#!/usr/bin/env bash
# Lance le bridge Kinect -> Unity avec un environnement uv prêt à l'emploi.
#
# `uv sync` suffit : freenect est une vraie dépendance du projet (compilée dans le venv depuis
# les sources), plus besoin de --system-site-packages pour aller chercher le binding système.
# Prérequis machine : un compilateur C et les headers libfreenect (paquet `libfreenect`).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

uv sync --quiet

if [ ! -f "models/pose_landmarker_lite.task" ]; then
    echo "Téléchargement du modèle PoseLandmarker (lite)..."
    mkdir -p models
    curl -sL -o models/pose_landmarker_lite.task \
        "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task"
fi

# `exec .venv/bin/python` et non `uv run` : Python doit REMPLACER le shell dans le même PID.
# KinectBridgeLauncher.cs tue ce PID par SIGKILL, qu'un `uv run` parent ne pourrait pas relayer
# à son enfant — le bridge survivrait en zombie, gardant la Kinect et le port UDP occupés.
exec .venv/bin/python -m kibird_bridge.bridge "$@"
