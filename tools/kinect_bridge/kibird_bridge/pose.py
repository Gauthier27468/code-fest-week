"""Wrapper MediaPipe Tasks (PoseLandmarker) -> 9 articulations utiles au gameplay.

RunningMode.VIDEO et non IMAGE : le mode VIDEO active le tracker interne de MediaPipe
(ROI + lissage temporel d'une frame a l'autre), a condition de lui fournir un timestamp
strictement croissant a chaque appel. En mode IMAGE chaque frame est isolee, ce qui produit
un jitter frame-a-frame et rend `min_tracking_confidence` inoperant.
"""
from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

# Index des 33 landmarks MediaPipe Pose correspondant a nos 9 articulations.
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
    x: float  # normalise [0,1], origine en haut a gauche de l'image
    y: float
    z: float  # profondeur relative MediaPipe, non utilisee pour la distance
    visibility: float


class PoseEstimator:
    """Detecte jusqu'a `num_poses` personnes et extrait les 9 articulations de chacune.

    Le choix de la personne a suivre est delegue a tracking.PlayerTracker.
    """

    def __init__(self, model_path: str | Path, num_poses: int = 3) -> None:
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
        self._start_monotonic = time.monotonic()
        self._last_timestamp_ms = -1

    def detect(self, rgb_frame: np.ndarray) -> list[dict[str, PoseLandmark]]:
        """rgb_frame : image RGB (H, W, 3) uint8 -> liste de {nom_articulation: PoseLandmark}."""
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
