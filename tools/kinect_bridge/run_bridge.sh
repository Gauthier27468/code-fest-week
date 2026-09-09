#!/usr/bin/env bash
# Lance le bridge Kinect -> Unity avec un environnement uv prêt à l'emploi.
#
# --system-site-packages est OBLIGATOIRE : `freenect` (bindings libfreenect) est installé
# au niveau système Python (/usr/lib/python3.*/site-packages/freenect.so) et n'existe pas
# sur PyPI. Sans ce flag, `import freenect` échoue silencieusement dans le venv.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

VENV=".venv"

if [ ! -d "$VENV" ]; then
    echo "Création du venv (--system-site-packages, pour l'accès à freenect)..."
    uv venv --system-site-packages "$VENV"
    uv pip install --python "$VENV/bin/python" -r requirements.txt
fi

if [ ! -f "models/pose_landmarker_lite.task" ]; then
    echo "Téléchargement du modèle PoseLandmarker (lite)..."
    mkdir -p models
    curl -sL -o models/pose_landmarker_lite.task \
        "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task"
fi

exec "$VENV/bin/python" -m kibird_bridge.bridge "$@"
