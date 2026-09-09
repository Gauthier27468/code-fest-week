"""Wrapper MediaPipe Tasks (PoseLandmarker) -> 9 articulations utiles au gameplay.

Utilise l'API Tasks (et non `mediapipe.solutions.pose`, l'API historique) : elle expose
`num_poses`, nécessaire pour détecter plusieurs personnes et laisser `tracking.PlayerTracker`
choisir laquelle suivre.

⚠️ Version de mediapipe figée à 0.10.35 dans requirements.txt. NE PAS passer à 1.0.x : sa
nouvelle architecture (`libmediapipe.so` chargée via ctypes) tue le process entier par SIGKILL
dans ses constructeurs statiques, avant même le moindre appel d'API. Voir README.md, section
"Blocage mediapipe 1.0.1 (résolu)".

⚠️ RunningMode.VIDEO, pas IMAGE : en mode IMAGE chaque frame est traitée comme une photo
isolée, sans aucun lien avec la précédente — c'est la cause classique du jitter frame-à-frame
signalé en JPO (bras immobiles détectés comme un battement). Le mode VIDEO active le tracker
interne de MediaPipe (ROI + lissage temporel des landmarks d'une frame à l'autre), à la seule
condition de lui fournir un timestamp strictement croissant à chaque appel — cf. `detect()`.
Signe qu'IMAGE était un choix par erreur : `min_tracking_confidence` ci-dessous ne fait
strictement rien en mode IMAGE, il ne s'applique qu'au tracker du mode VIDEO/LIVE_STREAM.
"""
from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

# Index des landmarks MediaPipe Pose (33 points) correspondant à nos 9 articulations utiles.
# Référence : https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker
_LANDMARK_INDEX = {
    "NOSE": 0,
    "L_SHOULDER": 11,
    "R_SHOULDER": 12,
    "L_ELBOW": 13,
    "R_ELBOW": 14,
    "L_WRIST": 15,
    "R_WRIST": 16,
    "L_HIP": 23,
    "R_HIP": 24,
}


@dataclass
class PoseLandmark:
    x: float  # normalisé [0,1], origine en haut à gauche de l'image
    y: float
    z: float  # profondeur relative MediaPipe (non utilisée pour la distance, cf. capture.py)
    visibility: float  # confiance MediaPipe [0,1]


class PoseEstimator:
    """Détecte jusqu'à `num_poses` personnes et extrait les 9 articulations de chacune.

    Le choix de la personne à suivre (verrouillage) est délégué à `tracking.PlayerTracker` :
    ce module se contente d'exposer toutes les détections, triées par proéminence MediaPipe.
    """

    def __init__(self, model_path: str | Path, num_poses: int = 3) -> None:
        # Import différé : permet d'utiliser capture.py/tracking.py/gestures.py/protocol.py
        # sans dépendre de mediapipe (utile tant que le blocage ci-dessus n'est pas résolu).
        import mediapipe as mp
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision

        self._mp = mp
        base_options = mp_python.BaseOptions(model_asset_path=str(model_path))
        options = vision.PoseLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.VIDEO,
            num_poses=num_poses,
            min_pose_detection_confidence=0.5,
            min_pose_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self._landmarker = vision.PoseLandmarker.create_from_options(options)
        # Horloge dédiée (monotonic, jamais affectée par un ajustement NTP) pour les timestamps
        # exigés par le mode VIDEO. `_last_timestamp_ms` garantit la stricte croissance exigée
        # par MediaPipe même si deux frames arrivent la même milliseconde (capture rapide).
        self._start_monotonic = time.monotonic()
        self._last_timestamp_ms = -1

    def detect(self, rgb_frame: np.ndarray) -> list[dict[str, PoseLandmark]]:
        """rgb_frame : image RGB (H, W, 3) uint8. Retourne une liste de squelettes détectés,
        chacun étant un dict {nom_articulation: PoseLandmark}.
        """
        mp_image = self._mp.Image(image_format=self._mp.ImageFormat.SRGB, data=rgb_frame)
        timestamp_ms = int((time.monotonic() - self._start_monotonic) * 1000)
        if timestamp_ms <= self._last_timestamp_ms:
            timestamp_ms = self._last_timestamp_ms + 1
        self._last_timestamp_ms = timestamp_ms
        result = self._landmarker.detect_for_video(mp_image, timestamp_ms)

        skeletons = []
        for pose_landmarks in result.pose_landmarks:
            joints = {}
            for name, idx in _LANDMARK_INDEX.items():
                lm = pose_landmarks[idx]
                joints[name] = PoseLandmark(x=lm.x, y=lm.y, z=lm.z, visibility=lm.visibility)
            skeletons.append(joints)
        return skeletons

    def close(self) -> None:
        self._landmarker.close()
