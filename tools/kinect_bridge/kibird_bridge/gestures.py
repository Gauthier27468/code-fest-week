"""Traduction des articulations en commandes de vol normalisees.

Unity ne recoit que des floats deja normalises et lisses (lean, lift, throttle, glide),
directement consommables comme un axe d'input : aucune logique de geste cote Unity.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field


class OneEuroFilter:
    """Lisse fort les gestes lents sans ajouter de retard perceptible sur les gestes rapides."""

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


def _deadzone(v: float, threshold: float, max_range: float) -> float:
    """Zone morte, puis renormalisation du reste de la plage sur [-1, 1].

    `max_range` est l'echelle de `v` (degres, metres...) : diviser par `(max_range - threshold)`
    et non par `(1.0 - threshold)`, qui inverserait le signe des que `threshold > 1`.
    """
    if abs(v) < threshold:
        return 0.0
    sign = 1.0 if v > 0 else -1.0
    return sign * (abs(v) - threshold) / (max_range - threshold)


@dataclass
class GestureConfig:
    lean_deadzone_deg: float = 5.0
    # `lean` vient de l'ecart de hauteur des poignets (ailerons), pas du buste : plus de course
    # qu'une inclinaison de buste, donc une plage plus large pour ne pas saturer trop tot.
    lean_max_deg: float = 45.0

    lift_window_s: float = 0.4  # duree de decroissance de l'impulsion de battement
    lift_trigger_speed: float = 0.5  # vitesse verticale descendante signant un battement
    # Frames consecutives au-dessus du seuil avant de declencher : un pic isole ne suffit pas.
    # Plus la valeur est grande, moins de faux positifs mais +1/30s de latence par frame.
    lift_trigger_frames: int = 3

    # Ecart poignet/epaule, en largeurs d'epaules, au-dela duquel glide vaut 0.
    glide_arm_range_shoulders: float = 1.4
    # Marge de confort SOUS la ligne d'epaules, en largeurs d'epaules, ou glide vaut encore 1.
    # Tenir les bras pile a hauteur d'epaules pendant toute une partie fatigue vite : 0.35
    # correspond a ~15 deg sous l'horizontale, une posture ailes deployees tenable plusieurs
    # minutes. Au-dessus de la ligne d'epaules, glide vaut 1 quoi qu'il arrive.
    glide_full_drop_shoulders: float = 0.35
    # Exposant de la courbe de reponse de glide : aplatit pres de glide=1 (un leger affaissement
    # des bras reste du plane plein) et creuse pres de glide=0.
    glide_falloff_power: float = 2.2

    # Taux de chute applique a `lift` hors battement, interpole lineairement sur `glide`.
    glide_full_sink: float = -0.08   # bras a l'horizontale : plane quasi plat
    glide_none_dive: float = -0.55   # bras le long du corps : pique net, pas un decrochage

    throttle_deadzone_m: float = 0.15
    throttle_range_m: float = 1.0  # +-1m autour de la distance neutre = +-1 en sortie


@dataclass
class GestureState:
    """Un etat par joueur verrouille, maintenu d'un appel a l'autre."""

    neutral_distance_m: float | None = None  # fixe a la calibration
    glide_value: float = 0.0
    lift_impulse: float = 0.0
    lift_impulse_started_at: float | None = None
    lean_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    throttle_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    glide_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    _prev_wrist_y: tuple[float, float] | None = None
    _prev_t: float | None = None
    _flap_streak: int = 0


@dataclass
class GestureOutput:
    lean: float
    lift: float
    throttle: float
    glide: float


def _arm_raise(l_wrist_y, r_wrist_y, l_shoulder_y, r_shoulder_y, shoulder_width: float,
               max_range_shoulders: float, falloff_power: float = 1.0,
               full_drop_shoulders: float = 0.0) -> float:
    """Hauteur des bras : 0.0 = le long du corps, 1.0 = ailes deployees (ou plus haut).

    Normalise par la largeur d'epaules a l'image, qui varie avec la distance au capteur
    exactement comme le reste du squelette : le geste reste calibre pareil a 1m ou a 4m.

    `y` croit vers le bas, donc un ecart positif = poignets SOUS la ligne d'epaules. Deux
    positions valent le plane plein : bras plus hauts que les epaules (lever davantage ne doit
    jamais penaliser le joueur), et bras abaisses de moins de `full_drop_shoulders`. Sans ce
    plateau il faudrait tenir les bras pile a l'horizontale toute la partie, ce qui fatigue
    trop vite pour un flux continu de visiteurs.
    """
    wrist_mid_y = (l_wrist_y + r_wrist_y) / 2.0
    shoulder_mid_y = (l_shoulder_y + r_shoulder_y) / 2.0
    unit = max(shoulder_width, 1e-6)
    drop = wrist_mid_y - shoulder_mid_y
    plateau = unit * full_drop_shoulders
    if drop <= plateau:
        return 1.0

    # La course restante part du bas du plateau : le pique plein reste atteint a la meme
    # hauteur de bras qu'avant (max_range_shoulders), seul le haut de la courbe s'aplatit.
    scale = max(unit * max_range_shoulders - plateau, 1e-6)
    ratio = _clamp((drop - plateau) / scale, 0.0, 1.0)
    return _clamp(1.0 - ratio ** falloff_power, 0.0, 1.0)


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
    """Coordonnees normalisees image : x croit vers la droite, y croit vers le bas.

    Convention MediaPipe : `L_*` designe le cote anatomique du sujet. Une personne face a la
    camera a donc son epaule gauche du cote droit de l'image (`l_shoulder[0] > r_shoulder[0]`).
    """

    # lean : inclinaison des bras facon ailerons. dx est oriente L->R (donc positif) pour que
    # l'angle reste petit autour de la posture neutre.
    dx = l_wrist[0] - r_wrist[0]
    dy = l_wrist[1] - r_wrist[1]
    angle_deg = math.degrees(math.atan2(dy, dx))
    # angle > 0 <=> poignet gauche du sujet plus bas <=> le sujet penche ses ailes vers SA
    # gauche, ce qui doit envoyer l'oiseau a gauche de l'ecran (lean < 0).
    lean_raw = _clamp(-_deadzone(angle_deg, config.lean_deadzone_deg, config.lean_max_deg))
    lean = _clamp(state.lean_filter(lean_raw, now))

    # glide : hauteur des bras, continue, lissee par le meme filtre que lean et throttle.
    shoulder_width = abs(l_shoulder[0] - r_shoulder[0])
    target = _arm_raise(l_wrist[1], r_wrist[1], l_shoulder[1], r_shoulder[1],
                        shoulder_width, config.glide_arm_range_shoulders,
                        config.glide_falloff_power, config.glide_full_drop_shoulders)
    state.glide_value = _clamp(state.glide_filter(target, now), 0.0, 1.0)

    # lift : comme un vrai oiseau, c'est le battement VERS LE BAS qui pousse sur l'air.
    # `vy > 0` = poignets qui descendent ; un `abs(vy)` declencherait aussi sur la remontee
    # des bras, qui n'est qu'un rearmement sans poussee.
    if state._prev_wrist_y is not None and state._prev_t is not None and dt > 1e-6:
        avg_wrist_y = (l_wrist[1] + r_wrist[1]) / 2.0
        prev_avg_y = (state._prev_wrist_y[0] + state._prev_wrist_y[1]) / 2.0
        vy = (avg_wrist_y - prev_avg_y) / dt
        if vy > config.lift_trigger_speed:
            state._flap_streak += 1
        else:
            state._flap_streak = 0
        if state._flap_streak >= config.lift_trigger_frames:
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

    # Chute liee a la posture des bras. Ne PAS ecrire `max(lift_impulse, sink)` : lift_impulse
    # vaut 0.0 au repos et sink est toujours negatif, ce qui annulerait tout le plane/pique.
    sink = config.glide_none_dive + (config.glide_full_sink - config.glide_none_dive) * state.glide_value
    lift = _clamp(state.lift_impulse if state.lift_impulse > 1e-3 else sink)

    # throttle : avancer = se rapprocher = distance plus petite que la neutre = throttle positif.
    if state.neutral_distance_m is None:
        throttle_raw = 0.0
    else:
        delta = state.neutral_distance_m - distance_m
        throttle_raw = _clamp(_deadzone(delta, config.throttle_deadzone_m, config.throttle_range_m))
    throttle = _clamp(state.throttle_filter(throttle_raw, now))

    return GestureOutput(lean=lean, lift=lift, throttle=throttle, glide=state.glide_value)
