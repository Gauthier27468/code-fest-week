import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge.gestures import GestureConfig, GestureState, update_gestures, OneEuroFilter, _deadzone


def test_deadzone():
    assert _deadzone(2.0, 5.0) == 0.0
    assert _deadzone(0.0, 5.0) == 0.0
    assert abs(_deadzone(-2.0, 5.0)) < 1e-9


def test_one_euro_first_call_passthrough():
    f = OneEuroFilter()
    assert f(1.0, 0.0) == 1.0


def test_one_euro_smooths_noise():
    """Un signal bruité doit ressortir plus lisse (variance réduite) après filtrage."""
    import random
    random.seed(42)
    f = OneEuroFilter(freq=30.0, min_cutoff=1.0, beta=0.0)
    t = 0.0
    raw_vals, filtered_vals = [], []
    for i in range(100):
        t += 1 / 30.0
        raw = 0.0 + random.uniform(-0.3, 0.3)  # signal stationnaire bruité
        raw_vals.append(raw)
        filtered_vals.append(f(raw, t))
    import statistics
    assert statistics.pstdev(filtered_vals) < statistics.pstdev(raw_vals)


# ⚠️ Convention MediaPipe respectée dans TOUS les tests ci-dessous : `L_*` est le côté
# anatomique du sujet, donc du côté DROIT de l'image quand il fait face à la caméra.
# Autrement dit l_shoulder[0] > r_shoulder[0]. Des coordonnées inversées masqueraient
# une saturation permanente de `lean` (bug réel constaté sur image réelle).


def test_neutral_stance_no_lean_or_throttle_false_positive():
    """Posture neutre (bras le long du corps) pendant 30 frames : direction et vitesse au repos.

    L'altitude, elle, DOIT réagir (piqué franc) : voir test_arms_down_dive juste après.
    """
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    t = 0.0
    for _ in range(30):
        t += 1 / 30.0
        out = update_gestures(
            state, cfg, now=t, dt=1 / 30.0,
            l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
            l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),  # bras le long du corps (y=0.6 >> épaules y=0.3)
            distance_m=2.0,
        )
    assert abs(out.lean) < 0.05, f"posture droite doit donner lean~0, obtenu {out.lean}"
    assert abs(out.throttle) < 0.05


def test_arms_down_dive():
    """Bras le long du corps = piqué franc, PAS un plané léger et surtout pas lift=0.

    Verrouille la correction d'un bug réel : `max(lift_impulse, sink)` avec lift_impulse=0.0
    au repos (jamais négatif) écrasait silencieusement tout le plané/piqué, quelle que soit
    la posture des bras — l'oiseau ne perdait jamais d'altitude en vol plané.
    """
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    t = 0.0
    out = None
    for _ in range(30):
        t += 1 / 30.0
        out = update_gestures(
            state, cfg, now=t, dt=1 / 30.0,
            l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
            l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),  # bras le long du corps
            distance_m=2.0,
        )
    assert out.glide < 0.05, f"bras le long du corps doit donner glide~0, obtenu {out.glide}"
    assert out.lift < -0.9, f"bras le long du corps doit piquer franc (lift proche de -1), obtenu {out.lift}"


def test_partial_arm_raise_gives_intermediate_sink():
    """Bras à mi-hauteur (ni le long du corps, ni à l'horizontale) : plané ET chute intermédiaires.

    C'est le point central de la continuité demandée : glide et lift ne sont pas des valeurs
    tout-ou-rien, ils suivent la hauteur réelle des bras entre les deux postures extrêmes.
    """
    cfg = GestureConfig()

    def settle(l_wrist_y, r_wrist_y):
        state = GestureState(neutral_distance_m=2.0)
        t = 0.0
        out = None
        for _ in range(60):  # 2s, largement assez pour que le lissage (3/s) rattrape la cible
            t += 1 / 30.0
            out = update_gestures(
                state, cfg, now=t, dt=1 / 30.0,
                l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
                l_wrist=(0.58, l_wrist_y), r_wrist=(0.42, l_wrist_y),
                distance_m=2.0,
            )
        return out

    down = settle(0.6, 0.6)   # bras le long du corps
    mid = settle(0.45, 0.45)  # à mi-chemin entre épaules (0.3) et bras baissés (0.6)
    up = settle(0.3, 0.3)     # bras tendus à l'horizontale (hauteur d'épaules)

    assert down.glide < up.glide, "monter les bras doit augmenter glide"
    assert down.glide < mid.glide < up.glide, f"position intermédiaire doit donner un glide intermédiaire, obtenu {mid.glide}"
    assert down.lift < mid.lift < up.lift,         f"la chute doit être intermédiaire entre piqué et plané, obtenu down={down.lift} mid={mid.lift} up={up.lift}"
    assert mid.lift < -0.05, "à mi-hauteur, l'oiseau doit quand même perdre de l'altitude"


def test_flap_overrides_dive():
    """Un battement doit faire monter l'oiseau même bras le long du corps (donc en plein piqué)."""
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    # Première frame : établit une position de poignet de référence, sans vitesse encore mesurable.
    update_gestures(
        state, cfg, now=0.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),
        distance_m=2.0,
    )
    # Battement soutenu sur 3 frames consécutives (lift_trigger_frames) : les poignets montent
    # vite (mouvement vers le haut = y qui décroît) et le restent, contrairement à un pic isolé.
    update_gestures(
        state, cfg, now=1 / 30.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.4), r_wrist=(0.42, 0.4),
        distance_m=2.0,
    )
    update_gestures(
        state, cfg, now=2 / 30.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.2), r_wrist=(0.42, 0.2),
        distance_m=2.0,
    )
    out = update_gestures(
        state, cfg, now=3 / 30.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.0), r_wrist=(0.42, 0.0),
        distance_m=2.0,
    )
    assert out.lift > 0.9, f"un battement doit donner une impulsion de montée forte, obtenu {out.lift}"


def test_fast_downward_arm_movement_does_not_trigger_flap():
    """Bras qui descendent vite (transition rapide vers le piqué) ne doivent PAS déclencher
    un battement/montée : seul un mouvement ASCENDANT rapide doit compter. Verrouille un bug
    réel où `abs(vy)` déclenchait un battement sur n'importe quel mouvement rapide, montant ou
    descendant — une cause plausible du "trop de lift" rapporté en JPO."""
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    update_gestures(
        state, cfg, now=0.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.3), r_wrist=(0.42, 0.3),  # bras à l'horizontale, en position de départ
        distance_m=2.0,
    )
    out = None
    for wrist_y in (0.5, 0.7, 0.9):  # les bras descendent vite vers le long du corps
        out = update_gestures(
            state, cfg, now=state._prev_t + 1 / 30.0, dt=1 / 30.0,
            l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
            l_wrist=(0.58, wrist_y), r_wrist=(0.42, wrist_y),
            distance_m=2.0,
        )
    assert state._flap_streak == 0, "un mouvement descendant rapide ne doit jamais compter comme un battement"
    assert out.lift < 0, f"bras descendant vite = piqué, pas montée ; obtenu lift={out.lift}"


def test_single_frame_velocity_spike_does_not_trigger_flap():
    """Un pic de vitesse isolé (bruit de détection sur une seule frame, bras par ailleurs
    immobiles) ne doit PAS être pris pour un battement — c'est le bug rapporté en JPO
    ("bras baissés, ça détecte des battements inexistants")."""
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    update_gestures(
        state, cfg, now=0.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),
        distance_m=2.0,
    )
    # Un unique sursaut (jitter de détection sur une seule frame) : la vitesse dépasse le
    # seuil, mais une seule fois — le debounce (lift_trigger_frames=3) doit l'ignorer.
    out = update_gestures(
        state, cfg, now=1 / 30.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.55), r_wrist=(0.42, 0.55),  # vy ≈ 1.5, largement au-dessus du seuil
        distance_m=2.0,
    )
    assert out.lift < -0.9, f"un sursaut d'une frame ne doit pas déclencher de battement, obtenu {out.lift}"
    assert state._flap_streak == 1, "le sursaut doit être compté, juste pas encore déclenché"


def test_real_world_upright_coordinates_give_no_lean():
    """Coordonnées relevées sur une VRAIE détection MediaPipe (personne debout, droite).

    C'est le cas qui a révélé le bug : avec dx calculé à l'envers, atan2 renvoyait ~174°
    et lean saturait à +1.0 pour quelqu'un de parfaitement immobile.
    """
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.3)
    out = update_gestures(
        state, cfg, now=1.0, dt=1 / 30.0,
        l_shoulder=(0.541, 0.482), r_shoulder=(0.455, 0.491),
        l_wrist=(0.698, 0.469), r_wrist=(0.298, 0.465),
        distance_m=2.3,
    )
    assert abs(out.lean) < 0.2, f"personne droite doit donner lean~0, obtenu {out.lean}"


def test_glide_pose_rises_to_one():
    cfg = GestureConfig()
    state = GestureState(neutral_distance_m=2.0)
    t = 0.0
    out = None
    for _ in range(60):  # 2s à 30Hz, largement suffisant pour que le filtre One Euro converge
        t += 1 / 30.0
        out = update_gestures(
            state, cfg, now=t, dt=1 / 30.0,
            l_shoulder=(0.7, 0.4), r_shoulder=(0.3, 0.4),
            l_wrist=(0.9, 0.4), r_wrist=(0.1, 0.4),  # bras tendus, même hauteur que les épaules
            distance_m=2.0,
        )
    assert out.glide > 0.95


def test_lean_sign_matches_player_intent():
    """Le joueur penche vers SA gauche -> l'oiseau doit aller à GAUCHE de l'écran (lean < 0).

    Verrouille la convention consommée par MoveBird.GetInput() côté Unity, où x = +1 est
    la droite de l'écran.
    """
    cfg = GestureConfig()

    # Penche vers sa gauche : son épaule gauche descend (y plus grand)
    state_l = GestureState(neutral_distance_m=2.0)
    out_left = update_gestures(
        state_l, cfg, now=1.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.5), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.7), r_wrist=(0.42, 0.6),
        distance_m=2.0,
    )
    assert out_left.lean < -0.1, f"penché à sa gauche doit donner lean < 0, obtenu {out_left.lean}"

    # Penche vers sa droite : son épaule droite descend
    state_r = GestureState(neutral_distance_m=2.0)
    out_right = update_gestures(
        state_r, cfg, now=1.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.5),
        l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.7),
        distance_m=2.0,
    )
    assert out_right.lean > 0.1, f"penché à sa droite doit donner lean > 0, obtenu {out_right.lean}"


def test_throttle_closer_is_faster():
    """AGENTS.md : avancer vers la Kinect = AUGMENTER la vitesse -> throttle positif."""
    cfg = GestureConfig()
    neutral = 2.0

    state_near = GestureState(neutral_distance_m=neutral)
    out_near = update_gestures(
        state_near, cfg, now=1.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),
        distance_m=1.2,  # le joueur a avancé
    )
    assert out_near.throttle > 0, f"avancer doit accélérer (throttle > 0), obtenu {out_near.throttle}"

    state_far = GestureState(neutral_distance_m=neutral)
    out_far = update_gestures(
        state_far, cfg, now=1.0, dt=1 / 30.0,
        l_shoulder=(0.6, 0.3), r_shoulder=(0.4, 0.3),
        l_wrist=(0.58, 0.6), r_wrist=(0.42, 0.6),
        distance_m=3.0,  # le joueur a reculé
    )
    assert out_far.throttle < 0, f"reculer doit ralentir (throttle < 0), obtenu {out_far.throttle}"


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
