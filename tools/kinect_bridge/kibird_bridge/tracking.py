"""Verrouillage du joueur, zone valide, delai de grace.

MediaPipe Pose n'expose aucun identifiant stable entre frames : le verrouillage est fait
ici par heuristique (gate de distance + continuite spatiale), pas par un ID de squelette.
"""
from __future__ import annotations

import time
from dataclasses import dataclass


@dataclass
class TrackingConfig:
    min_distance_m: float = 1.0
    max_distance_m: float = 4.0
    grace_period_s: float = 2.5
    max_jump_m: float = 0.5  # saut max du milieu des hanches entre 2 frames valides


@dataclass
class TrackingResult:
    in_zone: bool
    player_present: bool  # True tant qu'on est dans le delai de grace
    is_new_lock: bool  # True uniquement au tick ou le verrouillage vient d'etre pris


class PlayerTracker:
    """Un tracker par processus bridge (mono-joueur)."""

    def __init__(self, config: TrackingConfig | None = None) -> None:
        self.config = config or TrackingConfig()
        self._locked = False
        self._last_hip_x: float | None = None
        self._last_hip_y: float | None = None
        self._last_valid_time: float | None = None

    @property
    def locked(self) -> bool:
        return self._locked

    def reset(self) -> None:
        self._locked = False
        self._last_hip_x = None
        self._last_hip_y = None
        self._last_valid_time = None

    def update(
        self,
        distance_m: float,
        hip_mid_x: float | None,
        hip_mid_y: float | None,
        now: float | None = None,
    ) -> TrackingResult:
        """A appeler une fois par frame, meme sans pose detectee (hip_mid_x/y=None)."""
        now = now if now is not None else time.time()
        cfg = self.config

        in_zone = cfg.min_distance_m <= distance_m <= cfg.max_distance_m
        has_pose = hip_mid_x is not None and hip_mid_y is not None

        candidate_valid = in_zone and has_pose

        # Un saut trop grand n'est pas *cette* personne.
        if candidate_valid and self._locked and self._last_hip_x is not None:
            jump = ((hip_mid_x - self._last_hip_x) ** 2 + (hip_mid_y - self._last_hip_y) ** 2) ** 0.5
            if jump > cfg.max_jump_m:
                candidate_valid = False

        is_new_lock = False

        if candidate_valid:
            if not self._locked:
                is_new_lock = True
            self._locked = True
            self._last_hip_x, self._last_hip_y = hip_mid_x, hip_mid_y
            self._last_valid_time = now
            return TrackingResult(in_zone=True, player_present=True, is_new_lock=is_new_lock)

        if self._locked and self._last_valid_time is not None:
            elapsed = now - self._last_valid_time
            if elapsed <= cfg.grace_period_s:
                return TrackingResult(in_zone=in_zone, player_present=True, is_new_lock=False)

        self.reset()
        return TrackingResult(in_zone=in_zone, player_present=False, is_new_lock=False)
