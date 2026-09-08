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


def test_neutral_stance_no_false_positive():
    """Posture neutre (bras le long du corps) pendant 30 frames : aucune commande ne doit se déclencher."""
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
    assert abs(out.lift) < 0.05
    assert abs(out.throttle) < 0.05
    assert out.glide < 0.05


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
    cfg = GestureConfig(glide_rise_rate=2.0)
    state = GestureState(neutral_distance_m=2.0)
    t = 0.0
    out = None
    for _ in range(60):  # 2s à 30Hz, largement suffisant pour atteindre 1.0 à rise_rate=2/s
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
