"""Tests de la loi de commande du recentrage (autocenter).

Le moteur USB est simulé : ces tests vérifient les décisions (zone morte, cadence,
saturation, encodage du protocole, reprise après débranchement), pas le matériel.
"""
import sys
import types
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


class _FakeMotor:
    """Répond aux deux requêtes de contrôle du moteur Kinect et journalise les consignes."""

    def __init__(self):
        self.commands = []      # consignes reçues, en degrés
        self.angle_deg = 0.0
        self.status = 0x00      # arrêté
        self.read_fails = False

    def ctrl_transfer(self, request_type, request, value, index, arg):
        if request_type == 0xC0:  # lecture d'état
            if self.read_fails:
                raise OSError("[Errno 19] No such device")
            raw = int(round(self.angle_deg * 2)) & 0xFF
            return bytes([0, 0, 0, 20, 3, 22, 0, 218, raw, self.status])
        signed = value if value < 0x8000 else value - 0x10000
        self.commands.append(signed / 2.0)
        return 0


def _install(motor):
    """Substitue pyusb par un faux bus portant `motor` (ou rien si motor est None)."""
    core = types.ModuleType("usb.core")
    core.find = lambda **kwargs: motor
    pkg = types.ModuleType("usb")
    pkg.core = core
    sys.modules["usb"] = pkg
    sys.modules["usb.core"] = core

    import kibird_bridge.autocenter as ac
    ac._state.__init__()
    ac.reset()
    return ac


def _feed(ac, y, count, dt=1.5, start=0.0):
    """`count` appels à focus() espacés de dt, tête à l'ordonnée y."""
    now = start
    for _ in range(count):
        now += dt
        ac.focus((0.5, y), now=now)
    return now


def test_head_too_low_tilts_down_then_saturates():
    motor = _FakeMotor()
    ac = _install(motor)
    _feed(ac, 0.8, 8)
    assert motor.commands == [-6.0, -12.0, -18.0, -24.0, -28.0], motor.commands
    assert min(motor.commands) >= ac.TILT_MIN_DEG


def test_head_too_high_tilts_up_then_saturates():
    motor = _FakeMotor()
    ac = _install(motor)
    _feed(ac, 0.02, 8)
    assert motor.commands == [6.0, 12.0, 18.0, 24.0, 28.0], motor.commands
    assert max(motor.commands) <= ac.TILT_MAX_DEG


def test_deadzone_absorbs_jitter():
    motor = _FakeMotor()
    ac = _install(motor)
    now = 0.0
    for i in range(20):
        now += 1.5
        ac.focus((0.5, ac.TARGET_HEAD_Y + (0.03 if i % 2 else -0.03)), now=now)
    assert motor.commands == [], "le jitter MediaPipe ne doit pas faire bouger le moteur"


def test_rate_limited_at_30hz():
    motor = _FakeMotor()
    ac = _install(motor)
    _feed(ac, 0.9, 90, dt=1 / 30)  # 3 s de capture à 30 Hz
    expected = int(3.0 / ac.COMMAND_INTERVAL_S)
    assert len(motor.commands) == expected, motor.commands


def test_out_of_frame_head_is_ignored():
    motor = _FakeMotor()
    ac = _install(motor)
    now = 0.0
    for y in (-0.2, 1.4, 5.0):
        now += 1.5
        ac.focus((0.5, y), now=now)
    assert motor.commands == []


def test_half_degree_encoding_roundtrip():
    motor = _FakeMotor()
    ac = _install(motor)
    for degrees in (-28.0, -0.5, 0.0, 12.5, 28.0):
        assert ac.move_kinect(degrees, now=1.0) is True
    assert motor.commands == [-28.0, -0.5, 0.0, 12.5, 28.0]


def test_move_clamped_to_hardware_limits():
    motor = _FakeMotor()
    ac = _install(motor)
    ac.move_kinect(90.0, now=1.0)
    ac.move_kinect(-90.0, now=1.0)
    assert motor.commands == [ac.TILT_MAX_DEG, ac.TILT_MIN_DEG]


def test_reads_current_angle_and_status():
    motor = _FakeMotor()
    motor.angle_deg = 15.5
    motor.status = ac_moving = 0x04
    ac = _install(motor)
    assert ac.read_state(now=1.0) == (15.5, ac_moving)
    assert ac.current_tilt_deg(now=1.0) == 15.5


def test_uncalibrated_angle_reads_as_unknown():
    motor = _FakeMotor()
    motor.angle_deg = -64.0  # octet 0x80 : le moteur n'a pas trouvé sa référence
    ac = _install(motor)
    assert ac.read_state(now=1.0) is None


def test_missing_motor_never_raises():
    ac = _install(None)
    _feed(ac, 0.9, 5)
    assert ac._state.unavailable is False, "un moteur absent n'est pas une panne définitive"


def test_recovers_after_unplug():
    motor = _FakeMotor()
    ac = _install(motor)
    now = _feed(ac, 0.9, 2)
    assert len(motor.commands) == 2

    # Débranchement : lectures et commandes échouent, la poignée est lâchée.
    motor.read_fails = True
    ac._state.device = None
    sys.modules["usb.core"].find = lambda **kwargs: None
    now = _feed(ac, 0.9, 5, start=now)
    assert len(motor.commands) == 2, "aucune commande pendant l'absence"

    # Retour du périphérique, une fois passé le délai de redécouverte.
    motor.read_fails = False
    sys.modules["usb.core"].find = lambda **kwargs: motor
    _feed(ac, 0.9, 3, start=now + ac.REDISCOVER_INTERVAL_S + 1)
    assert len(motor.commands) > 2, "le moteur doit être repris après rebranchement"


def test_reset_forgets_player_state():
    motor = _FakeMotor()
    ac = _install(motor)
    _feed(ac, 0.9, 3)
    ac.reset()
    assert ac._state.smoothed_y is None
    assert ac._state.target_deg is None
    assert ac._state.device is motor, "reset ne doit pas lâcher le moteur"


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
