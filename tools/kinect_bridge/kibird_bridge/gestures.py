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
    # `lean` vient de l'écart de hauteur des poignets (bras type "ailerons"), pas du buste :
    # un bras qui monte et l'autre qui descend a naturellement plus de course qu'une simple
    # inclinaison du buste, donc une plage plus large que l'ancien réglage "épaules" (25°)
    # pour que la réponse reste graduée sur tout le mouvement au lieu de saturer trop tôt.
    # Point de départ à ajuster après test réel.
    lean_max_deg: float = 45.0
    lift_window_s: float = 0.4  # durée de décroissance de l'impulsion de battement
    lift_trigger_speed: float = 0.5  # vitesse verticale DESCENDANTE (unités normalisées/s) qui signe un battement
    lift_trigger_frames: int = 3  # frames CONSÉCUTIVES au-dessus du seuil avant de déclencher
    # (anti-jitter, complémentaire du mode VIDEO de MediaPipe et du filtrage par sens ci-dessus :
    # un pic isolé d'une ou deux frames ne suffit plus à déclencher un battement fantôme, il
    # faut un vrai mouvement descendant soutenu sur ~100ms. À ajuster après test réel : plus ce
    # nombre est grand, plus les faux positifs baissent mais plus la détection d'un vrai
    # battement prend de retard — chaque frame en plus coûte 1/30s sur le budget de latence.)

    # `glide` (0 = bras le long du corps, 1 = bras tendus à l'horizontale) est une mesure
    # CONTINUE de la hauteur des poignets par rapport aux épaules, pas un seuil tout-ou-rien :
    # un joueur qui lève les bras à mi-hauteur obtient un plané partiel, avec un taux de chute
    # intermédiaire (cf. glide_full_sink / glide_none_dive ci-dessous).
    glide_arm_range_shoulders: float = 1.4  # écart poignet/épaule (en largeurs d'épaules) pour glide=0

    # Courbe de réponse de `glide` (pas une simple droite) : humainement, un joueur qui croit
    # tendre les bras à l'horizontale les a presque toujours un peu plus bas que les épaules
    # (fatigue, imprécision du geste). Avec un mapping linéaire, ce petit écart — pourtant
    # anodin visuellement — coûtait déjà une chute perceptible. L'exposant aplatit la courbe
    # près de glide=1 (un léger affaissement reste presque du plané plein) et la creuse près de
    # glide=0 (bras vraiment bas -> la chute augmente vite), sans changer les bornes (1 quand
    # les poignets sont à hauteur d'épaule, 0 au-delà de `glide_arm_range_shoulders`).
    glide_falloff_power: float = 2.2

    # Taux de chute vertical appliqué à `lift` en l'absence de battement, interpolé linéairement
    # sur `glide` entre ces deux bornes.
    # glide_full_sink : plané à fond (bras à l'horizontale) -> quasi horizontal, l'oiseau ne
    # doit descendre que très légèrement (retour JPO : "presque rester droit verticalement mais
    # descendre un tout petit peu"). Proche de 0 mais pas nul : un plané qui ne fait *jamais*
    # perdre d'altitude ne se sent plus comme un plané.
    # glide_none_dive : bras le long du corps -> piqué, doit se sentir clairement descendre mais
    # sans être un décrochage brutal (retour JPO : moins violent que -1.0, qui saturait
    # immédiatement `lift` au clamp bas dès que les bras étaient baissés).
    glide_full_sink: float = -0.08   # bras à l'horizontale (glide=1) : plané quasi plat
    glide_none_dive: float = -0.55   # bras le long du corps (glide=0) : piqué net mais pas un crash

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
    glide_filter: OneEuroFilter = field(default_factory=OneEuroFilter)
    _prev_wrist_y: tuple[float, float] | None = None
    _prev_t: float | None = None
    _flap_streak: int = 0  # frames consécutives au-dessus du seuil de vitesse (debounce)


@dataclass
class GestureOutput:
    lean: float
    lift: float
    throttle: float
    glide: float


def _arm_raise(l_wrist_y, r_wrist_y, l_shoulder_y, r_shoulder_y, shoulder_width: float,
               max_range_shoulders: float, falloff_power: float = 1.0) -> float:
    """Hauteur des bras, continue : 0.0 = le long du corps, 1.0 = tendus à l'horizontale.

    Normalisé par la largeur d'épaules à l'image (pas un seuil fixe en coordonnées
    normalisées) : cette largeur varie avec la distance au capteur exactement comme le
    reste du squelette, donc le geste reste calibré pareil à 1m ou à 4m de la Kinect.

    `falloff_power` > 1 courbe la réponse (cf. GestureConfig.glide_falloff_power) : tolérant
    près de la posture cible (poignets à hauteur d'épaule), de plus en plus punitif au fur et
    à mesure que les bras descendent.
    """
    wrist_mid_y = (l_wrist_y + r_wrist_y) / 2.0
    shoulder_mid_y = (l_shoulder_y + r_shoulder_y) / 2.0
    offset = abs(wrist_mid_y - shoulder_mid_y)
    scale = max(shoulder_width, 1e-6) * max_range_shoulders
    ratio = _clamp(offset / scale, 0.0, 1.0)
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
    """Coordonnées normalisées image (x croît vers la droite, y croît vers le bas).

    ⚠️ Convention MediaPipe : `L_*` désigne le côté **anatomique** du sujet. Une personne qui
    fait face à la caméra a donc son épaule gauche du côté **droit de l'image** :
    `l_shoulder[0] > r_shoulder[0]`. Calculer `dx` dans l'autre sens donne un angle proche de
    180° pour un sujet parfaitement droit, et donc une saturation permanente de `lean`.
    """

    # --- lean : inclinaison des BRAS (pas du buste) — geste type "ailerons" ---
    # Bras droit qui monte + bras gauche qui descend = virage à gauche (lean < 0), et plus
    # l'écart est marqué, plus le virage est prononcé. Même construction géométrique que
    # l'ancienne version basée sur les épaules (angle de la ligne reliant les deux points),
    # simplement appliquée aux poignets : ça réutilise directement la logique de signe déjà
    # vérifiée par test_lean_sign_matches_player_intent.
    #
    # dx orienté L->R (donc positif, cf. convention MediaPipe ci-dessus) pour que l'angle
    # reste petit autour de la posture neutre (bras symétriques).
    dx = l_wrist[0] - r_wrist[0]
    dy = l_wrist[1] - r_wrist[1]
    angle_deg = math.degrees(math.atan2(dy, dx))  # 0° = poignets à la même hauteur
    # angle > 0 <=> poignet GAUCHE du sujet plus bas (donc bras gauche baissé, droit levé)
    # <=> le sujet "penche ses ailes" vers SA gauche, ce qui doit envoyer l'oiseau vers la
    # gauche de l'écran (lean < 0) : d'où le signe, identique à l'ancienne version épaules.
    lean_raw = _clamp(-_deadzone(angle_deg, config.lean_deadzone_deg, config.lean_max_deg))
    lean = _clamp(state.lean_filter(lean_raw, now))

    # --- glide : hauteur des bras, continue, lissée par le même filtre One Euro que lean et
    # throttle (moyenne mobile adaptative : lisse fort quand la posture est stable, sans retard
    # perceptible dès qu'elle change vraiment — cf. commentaire sur OneEuroFilter en tête de
    # fichier). Remplace l'ancienne rampe à vitesse fixe, incohérente avec le reste des gestes.
    shoulder_width = abs(l_shoulder[0] - r_shoulder[0])
    target = _arm_raise(l_wrist[1], r_wrist[1], l_shoulder[1], r_shoulder[1],
                         shoulder_width, config.glide_arm_range_shoulders,
                         config.glide_falloff_power)
    state.glide_value = _clamp(state.glide_filter(target, now), 0.0, 1.0)

    # --- lift : impulsion sur battement descendant (vitesse verticale des poignets) ---
    if state._prev_wrist_y is not None and state._prev_t is not None and dt > 1e-6:
        avg_wrist_y = (l_wrist[1] + r_wrist[1]) / 2.0
        prev_avg_y = (state._prev_wrist_y[0] + state._prev_wrist_y[1]) / 2.0
        vy = (avg_wrist_y - prev_avg_y) / dt  # y croît vers le bas -> vy>0 = mouvement vers le bas
        # Comme un vrai oiseau : c'est le battement VERS LE BAS qui pousse sur l'air et fait
        # monter, pas la remontée des bras (qui n'est que le geste de "réarmement" entre deux
        # battements, sans poussée). On détecte un mouvement descendant rapide et on le convertit
        # en impulsion positive (montée) qui décroît ensuite sur `lift_window_s`.
        # ⚠️ Sens du mouvement, pas seulement sa vitesse : `vy > 0` = poignets qui DESCENDENT
        # (y croît vers le bas dans le repère image). Un `abs(vy)` déclencherait un battement
        # sur n'importe quel mouvement rapide, y compris la remontée des bras entre deux
        # battements, qui ne doit donner aucune impulsion.
        if vy > config.lift_trigger_speed:
            state._flap_streak += 1
        else:
            state._flap_streak = 0
        # Debounce : un pic de vitesse isolé (bruit de détection sur une seule frame) ne
        # déclenche rien, il faut plusieurs frames consécutives — cf. commentaire sur
        # lift_trigger_frames dans GestureConfig.
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

    # Chute liée à la posture des bras, continue entre les deux bornes de config ci-dessus :
    # bras à l'horizontale (glide=1) -> plané net ; bras le long du corps (glide=0) -> piqué.
    # ⚠️ Ne PAS combiner avec `max(lift_impulse, sink)` : lift_impulse vaut 0.0 au repos (pas
    # négatif) et `max(0.0, sink)` renvoie alors 0.0 puisque sink est toujours négatif — ça
    # annulait silencieusement tout le plané/piqué en dehors d'un battement (bug réel constaté :
    # l'oiseau ne perdait jamais d'altitude en vol plané). Un battement prioritaire écrase donc
    # explicitement la chute le temps de son impulsion, au lieu de rivaliser avec elle via max().
    sink = config.glide_none_dive + (config.glide_full_sink - config.glide_none_dive) * state.glide_value
    lift = state.lift_impulse if state.lift_impulse > 1e-3 else sink
    lift = _clamp(lift)

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
