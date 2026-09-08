"""Traduction des articulations en commandes de vol normalisées (implementation_plan.md 3.3).

Les gestes sont calculés ici, côté Python : Unity ne reçoit que des floats déjà
normalisés et lissés (lean, lift, throttle, glide), directement consommables comme
un axe d'input clavier — aucune logique de geste ne doit fuiter côté Unity.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field


# --- Filtre One Euro -------------------------------------------------------
# Meilleur compromis jitter/latence qu'une moyenne glissante : lisse fort les gestes
# lents (posture stable) sans ajouter de retard perceptible sur les gestes rapides
# (battement d'ailes), contrairement à un EMA à fenêtre fixe.
class OneEuroFilter:
    def __init__(self, freq: float = 30.0, min_cutoff: float = 1.0, beta: float = 0.0, d_cutoff: float = 1.0):
        self.freq = freq
        self.min_cutoff = min_cutoff
        self.beta = beta
        self.d_cutoff = d_cutoff
        self._x_prev: float | None = None
        self._dx_prev = 0.0
        self._t_prev: float | None = None

    @staticmethod
    def _alpha(cutoff: float, freq: float) -> float:
        te = 1.0 / freq
        tau = 1.0 / (2 * math.pi * cutoff)
        return 1.0 / (1.0 + tau / te)

    def __call__(self, x: float, t: float) -> float:
        if self._t_prev is None:
            self._x_prev = x
            self._t_prev = t
            return x

        dt = max(t - self._t_prev, 1e-6)
        freq = 1.0 / dt

        dx = (x - self._x_prev) / dt
        a_d = self._alpha(self.d_cutoff, freq)
        dx_hat = a_d * dx + (1 - a_d) * self._dx_prev

        cutoff = self.min_cutoff + self.beta * abs(dx_hat)
        a = self._alpha(cutoff, freq)
        x_hat = a * x + (1 - a) * self._x_prev

        self._x_prev, self._dx_prev, self._t_prev = x_hat, dx_hat, t
        return x_hat


def _clamp(v: float, lo: float = -1.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, v))


def _deadzone(v: float, threshold: float, max_range: float = 1.0) -> float:
    """Applique une zone morte et renormalise le reste de la plage sur [-1, 1].

    `max_range` est l'échelle de `v` (ex: 25 pour des degrés, 1.0 m pour une distance) —
    diviser par `(max_range - threshold)` et non par `(1.0 - threshold)` : ce dernier
    n'a de sens que si `v` est déjà dans [-1, 1], sinon le signe peut s'inverser dès que
    `threshold > 1` (cas des degrés).
    """
    if abs(v) < threshold:
        return 0.0
    sign = 1.0 if v > 0 else -1.0
    return sign * (abs(v) - threshold) / (max_range - threshold)


@dataclass
class GestureConfig:
    lean_deadzone_deg: float = 5.0
    lean_max_deg: float = 25.0
    lift_window_s: float = 0.4  # durée de décroissance de l'impulsion de battement
    glide_shoulder_tolerance: float = 0.15  # tolérance relative (±15%) hauteur poignet/épaule
    glide_rise_rate: float = 2.0  # 1/s, vitesse de montée de `glide` vers 1.0
    throttle_deadzone_m: float = 0.15
    throttle_range_m: float = 1.0  # +-1m autour de la distance neutre = +-1 en sortie


@dataclass
class GestureState:
    """État à maintenir d'un appel à l'autre, un par joueur verrouillé."""

    neutral_distance_m: float | None = None  # fixé à la calibration (posture glide 3s)
    glide_value: float = 0.0
    lift_impulse: float = 0.0
    lift_impulse_started_at: float | None = None
    lean_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    throttle_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    _prev_wrist_y: tuple[float, float] | None = None
    _prev_t: float | None = None


@dataclass
class GestureOutput:
    lean: float
    lift: float
    throttle: float
    glide: float


def _is_glide_pose(l_wrist_y, r_wrist_y, l_shoulder_y, r_shoulder_y, tolerance: float) -> bool:
    """Bras tendus à l'horizontale : poignets à hauteur d'épaules (± tolérance relative)."""
    l_ok = abs(l_wrist_y - l_shoulder_y) <= tolerance
    r_ok = abs(r_wrist_y - r_shoulder_y) <= tolerance
    return l_ok and r_ok


def update_gestures(
    state: GestureState,
    config: GestureConfig,
    now: float,
    dt: float,
    l_shoulder: tuple[float, float],
    r_shoulder: tuple[float, float],
    l_wrist: tuple[float, float],
    r_wrist: tuple[float, float],
    distance_m: float,
) -> GestureOutput:
    """Coordonnées normalisées image (x croît vers la droite, y croît vers le bas).

    ⚠️ Convention MediaPipe : `L_*` désigne le côté **anatomique** du sujet. Une personne qui
    fait face à la caméra a donc son épaule gauche du côté **droit de l'image** :
    `l_shoulder[0] > r_shoulder[0]`. Calculer `dx` dans l'autre sens donne un angle proche de
    180° pour un sujet parfaitement droit, et donc une saturation permanente de `lean`.
    """

    # --- lean : angle de la ligne d'épaules ---
    # dx orienté L->R (donc positif, cf. convention ci-dessus) pour que l'angle reste petit
    # autour de la posture neutre.
    dx = l_shoulder[0] - r_shoulder[0]
    dy = l_shoulder[1] - r_shoulder[1]
    angle_deg = math.degrees(math.atan2(dy, dx))  # 0° = épaules parfaitement horizontales
    # angle > 0 <=> épaule gauche du sujet plus basse <=> le sujet penche vers SA gauche,
    # ce qui doit envoyer l'oiseau vers la gauche de l'écran (lean < 0) : d'où le signe.
    lean_raw = _clamp(-_deadzone(angle_deg, config.lean_deadzone_deg, config.lean_max_deg))
    lean = _clamp(state.lean_filter(lean_raw, now))

    # --- glide : posture bras tendus maintenue, montée progressive vers 1 ---
    is_glide = _is_glide_pose(l_wrist[1], r_wrist[1], l_shoulder[1], r_shoulder[1], config.glide_shoulder_tolerance)
    target = 1.0 if is_glide else 0.0
    step = config.glide_rise_rate * dt
    if state.glide_value < target:
        state.glide_value = min(target, state.glide_value + step)
    else:
        state.glide_value = max(target, state.glide_value - step)

    # --- lift : impulsion sur battement descendant (vitesse verticale des poignets) ---
    if state._prev_wrist_y is not None and state._prev_t is not None and dt > 1e-6:
        avg_wrist_y = (l_wrist[1] + r_wrist[1]) / 2.0
        prev_avg_y = (state._prev_wrist_y[0] + state._prev_wrist_y[1]) / 2.0
        vy = (avg_wrist_y - prev_avg_y) / dt  # y croît vers le bas -> vy<0 = mouvement vers le haut
        # Battement descendant = poignets qui descendent PUIS déclenchent une montée : on détecte
        # un mouvement vertical rapide et on convertit en impulsion positive (montée) qui décroît.
        speed = abs(vy)
        if speed > 0.5:  # seuil de détection d'un battement (unités normalisées/s)
            state.lift_impulse = 1.0
            state.lift_impulse_started_at = now

    if state.lift_impulse_started_at is not None:
        age = now - state.lift_impulse_started_at
        if age >= config.lift_window_s:
            state.lift_impulse = 0.0
            state.lift_impulse_started_at = None
        else:
            state.lift_impulse = 1.0 - (age / config.lift_window_s)

    state._prev_wrist_y = (l_wrist[1], r_wrist[1])
    state._prev_t = now

    # `glide` (plané, descente très lente) tire lift vers une légère valeur négative/nulle ;
    # un battement prioritaire écrase temporairement le plané.
    lift = _clamp(max(state.lift_impulse, -state.glide_value * 0.1))

    # --- throttle : distance relative à la distance neutre calibrée ---
    if state.neutral_distance_m is None:
        throttle_raw = 0.0
    else:
        # AGENTS.md : "Avancer/Reculer par rapport au Kinect -> Augmentation / Réduction de la
        # vitesse". Avancer = se rapprocher = distance PLUS PETITE que la neutre, et doit donner
        # un throttle POSITIF : d'où `neutral - distance` et non l'inverse.
        delta = state.neutral_distance_m - distance_m
        throttle_raw = _clamp(_deadzone(delta, config.throttle_deadzone_m, config.throttle_range_m))
    throttle = _clamp(state.throttle_filter(throttle_raw, now))

    return GestureOutput(lean=lean, lift=lift, throttle=throttle, glide=state.glide_value)
