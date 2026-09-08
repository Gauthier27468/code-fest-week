"""Wrapper MediaPipe Tasks (PoseLandmarker) -> 9 articulations utiles au gameplay.

Utilise l'API Tasks (et non `mediapipe.solutions.pose`, l'API historique) : elle expose
`num_poses`, nécessaire pour détecter plusieurs personnes et laisser `tracking.PlayerTracker`
choisir laquelle suivre.

⚠️ Version de mediapipe figée à 0.10.35 dans requirements.txt. NE PAS passer à 1.0.x : sa
nouvelle architecture (`libmediapipe.so` chargée via ctypes) tue le process entier par SIGKILL
dans ses constructeurs statiques, avant même le moindre appel d'API. Voir README.md, section
"Blocage mediapipe 1.0.1 (résolu)".
"""
from __future__ import annotations

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
            running_mode=vision.RunningMode.IMAGE,
            num_poses=num_poses,
            min_pose_detection_confidence=0.5,
            min_pose_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self._landmarker = vision.PoseLandmarker.create_from_options(options)

    def detect(self, rgb_frame: np.ndarray) -> list[dict[str, PoseLandmark]]:
        """rgb_frame : image RGB (H, W, 3) uint8. Retourne une liste de squelettes détectés,
        chacun étant un dict {nom_articulation: PoseLandmark}.
        """
        mp_image = self._mp.Image(image_format=self._mp.ImageFormat.SRGB, data=rgb_frame)
        result = self._landmarker.detect(mp_image)

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
