import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge.tracking import PlayerTracker, TrackingConfig


def test_lock_within_zone():
    t = PlayerTracker()
    r = t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    assert r.in_zone is True
    assert r.player_present is True
    assert r.is_new_lock is True
    assert t.locked is True


def test_reject_out_of_zone():
    t = PlayerTracker()
    r = t.update(distance_m=5.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    assert r.in_zone is False
    assert r.player_present is False
    assert t.locked is False

    r2 = t.update(distance_m=0.5, hip_mid_x=0.5, hip_mid_y=0.5, now=1.0)
    assert r2.in_zone is False
    assert r2.player_present is False


def test_grace_period_holds_then_expires():
    cfg = TrackingConfig(grace_period_s=2.5)
    t = PlayerTracker(cfg)
    t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    assert t.locked is True

    # Perte de pose (occlusion) à t=1.0 : encore dans le délai de grâce
    r = t.update(distance_m=0.0, hip_mid_x=None, hip_mid_y=None, now=1.0)
    assert r.player_present is True, "doit rester actif pendant le délai de grâce"

    # Toujours perdu à t=2.4 : encore dans le délai de grâce (2.5s)
    r2 = t.update(distance_m=0.0, hip_mid_x=None, hip_mid_y=None, now=2.4)
    assert r2.player_present is True

    # Perdu au-delà de 2.5s : reset
    r3 = t.update(distance_m=0.0, hip_mid_x=None, hip_mid_y=None, now=3.6)
    assert r3.player_present is False
    assert t.locked is False


def test_recovery_within_grace_period():
    """Le joueur revient avant expiration du délai de grâce : pas de nouveau lock."""
    t = PlayerTracker(TrackingConfig(grace_period_s=2.5))
    t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    t.update(distance_m=0.0, hip_mid_x=None, hip_mid_y=None, now=1.0)  # occlusion
    r = t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=1.5)  # retour
    assert r.player_present is True
    assert r.is_new_lock is False, "ne doit pas re-déclencher OnPlayerEntered"


def test_second_player_rejected_by_jump():
    """Un second corps qui apparaît loin du joueur verrouillé ne doit pas prendre le lock."""
    t = PlayerTracker(TrackingConfig(max_jump_m=0.5))
    t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    # Un autre squelette à 2m de distance mais très loin latéralement
    r = t.update(distance_m=2.0, hip_mid_x=5.5, hip_mid_y=0.5, now=0.1)
    assert r.player_present is True  # le tracker retombe sur le délai de grâce du joueur original
    assert r.is_new_lock is False


def test_new_lock_after_full_reset():
    t = PlayerTracker()
    t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=0.0)
    t.reset()
    r = t.update(distance_m=2.0, hip_mid_x=0.5, hip_mid_y=0.5, now=10.0)
    assert r.is_new_lock is True


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
