"""Recentrage vertical automatique de la Kinect sur la tete du joueur (moteur de tilt).

La Kinect n'a qu'un seul moteur, et il est vertical : le recentrage horizontal est
impossible cote materiel (c'est au joueur de se placer dans la zone au sol). Ce module
ne pilote donc que le tilt, a partir de l'ordonnee de la tete.

Pourquoi pyusb et pas freenect
------------------------------
`freenect.open_device()` reclame l'interface camera EN MEME TEMPS que le moteur. Or
capture.py detient deja cette interface via `sync_get_video()`, donc l'ouverture echoue :

    Failed to claim camera interface: LIBUSB_ERROR_BUSY
    open_device -> None

Dans l'autre sens c'est la capture qui meurt. Le binding Cython n'expose pas
`freenect_select_subdevices()`, qui permettrait de ne reclamer que DEVICE_MOTOR. Mais le
moteur est un peripherique USB *distinct* de la camera (045e:02b0 contre 045e:02ae) : on
lui parle donc en direct en USB, sans jamais toucher a la camera ni gener libfreenect.
Le protocole est celui de libfreenect (src/tilt.c).
"""
from __future__ import annotations

import struct
import time

# Peripherique moteur, distinct de la camera (02ae) et de l'audio (02ad).
MOTOR_VENDOR_ID = 0x045E
MOTOR_PRODUCT_ID = 0x02B0

# Requetes de controle USB du moteur (libfreenect src/tilt.c).
_REQ_SET_TILT = 0x31
_REQ_GET_STATE = 0x32
_TYPE_OUT = 0x40  # vendor, host -> device
_TYPE_IN = 0xC0   # vendor, device -> host
_STATE_LEN = 10

# Octet 9 de l'etat : ou en est le moteur.
TILT_STATUS_STOPPED = 0x00
TILT_STATUS_LIMIT = 0x01
TILT_STATUS_MOVING = 0x04

# Octet 8 (angle brut) : le moteur n'a pas encore trouve sa reference.
_ANGLE_UNKNOWN = -128

# Butees materielles du moteur : libfreenect accepte +/-31 deg, on garde une marge.
TILT_MIN_DEG = -10.0
TILT_MAX_DEG = 28.0

# Ordonnee visee pour la tete, en coordonnees image normalisees (0 = haut, 1 = bas).
# Au-dessus du centre : le reste du corps (epaules, hanches, poignets) doit tenir dessous,
# ce sont ces articulations qui alimentent le tracking et les gestes.
TARGET_HEAD_Y = 0.35

# Erreur en deca de laquelle on ne bouge pas. Sans cette zone morte, le jitter de MediaPipe
# suffit a declencher des corrections en permanence et le moteur oscille sans jamais se poser.
DEADZONE = 0.08

# Gain proportionnel : une erreur pleine echelle (0.5) donne ~14 deg de correction.
GAIN_DEG = 28.0
MAX_STEP_DEG = 6.0
# Le moteur ignore les corrections trop fines : en dessous, autant ne rien envoyer.
MIN_STEP_DEG = 2.0

# Le moteur met ~1s a parcourir sa course. L'octet de statut dit quand il a fini, mais il
# reste a 0 pendant les premieres dizaines de ms : ce delai minimal evite de prendre un
# "stopped" perime pour une fin de mouvement et d'empiler deux corrections.
COMMAND_INTERVAL_S = 1.2

# Lissage de l'ordonnee de la tete avant decision, pour la meme raison que la zone morte.
SMOOTHING = 0.25

# La Kinect disparait parfois du bus USB en cours de partie. On retente de la retrouver, mais
# pas a chaque frame : un scan USB rate coute ~1 ms, soit 3% du budget d'une frame a 30 Hz.
REDISCOVER_INTERVAL_S = 5.0


class _State:
    def __init__(self) -> None:
        self.device = None
        self.unavailable = False       # panne definitive (pyusb absent, permissions)
        self.last_discovery_at = 0.0   # derniere tentative de (re)decouverte du moteur
        self.warned = False            # ne pas repeter le meme diagnostic a chaque frame
        self.smoothed_y: float | None = None
        self.last_command_at = 0.0
        self.target_deg: float | None = None


_state = _State()


def _warn(message: str) -> None:
    if not _state.warned:
        print(f"[AUTOCENTER] {message}")
        _state.warned = True


def _device(now: float):
    """Poignee USB du moteur, ou None. Retente la decouverte apres un debranchement."""
    if _state.unavailable:
        return None
    if _state.device is not None:
        return _state.device
    if _state.last_discovery_at and now - _state.last_discovery_at < REDISCOVER_INTERVAL_S:
        return None
    _state.last_discovery_at = now

    try:
        import usb.core
    except ImportError:
        # Panne definitive : contrairement a un debranchement, reessayer n'y changera rien.
        _state.unavailable = True
        print("[AUTOCENTER] pyusb absent, auto-centrage desactive (uv sync).")
        return None

    try:
        device = usb.core.find(idVendor=MOTOR_VENDOR_ID, idProduct=MOTOR_PRODUCT_ID)
    except Exception as exc:  # noqa: BLE001 - l'auto-centrage ne doit jamais tuer la capture
        _warn(f"Scan USB impossible : {exc}")
        return None

    if device is None:
        _warn("Moteur Kinect introuvable sur le bus USB, auto-centrage en attente.")
        return None

    # Pas de set_configuration() : la configuration par defaut porte deja les requetes
    # vendor, et la (re)definir provoque un reset USB qui ferait tomber la capture video.
    _state.device = device
    _state.warned = False
    print("[AUTOCENTER] Moteur Kinect detecte, auto-centrage actif.")
    return device


def _drop_device(reason: str) -> None:
    """Oublie la poignee : elle sera reprise a la prochaine decouverte."""
    _state.device = None
    _warn(f"{reason} - nouvelle tentative dans {REDISCOVER_INTERVAL_S:.0f}s.")


def read_state(now: float | None = None) -> tuple[float, int] | None:
    """(angle en degres, octet de statut), ou None si le moteur est injoignable."""
    now = time.monotonic() if now is None else now
    device = _device(now)
    if device is None:
        return None
    try:
        raw = device.ctrl_transfer(_TYPE_IN, _REQ_GET_STATE, 0, 0, _STATE_LEN)
    except Exception as exc:  # noqa: BLE001
        _drop_device(f"Lecture de l'etat moteur impossible ({exc})")
        return None
    if len(raw) < _STATE_LEN:
        _drop_device(f"Etat moteur tronque ({len(raw)} octets)")
        return None
    angle_raw = struct.unpack("b", bytes(raw[8:9]))[0]
    if angle_raw == _ANGLE_UNKNOWN:
        return None  # moteur pas encore reference, l'angle mesure ne veut rien dire
    return angle_raw / 2.0, raw[9]


def current_tilt_deg(now: float | None = None) -> float | None:
    """Angle de tilt courant en degres, ou None si le moteur est injoignable."""
    state = read_state(now)
    return None if state is None else state[0]


def move_kinect(degrees: float, now: float | None = None) -> bool:
    """Envoie une consigne absolue de tilt, bornee aux butees. True si l'ordre est passe."""
    now = time.monotonic() if now is None else now
    device = _device(now)
    if device is None:
        return False
    degrees = max(TILT_MIN_DEG, min(TILT_MAX_DEG, degrees))
    # Le moteur raisonne en demi-degres, sur 16 bits signes.
    value = int(round(degrees * 2)) & 0xFFFF
    try:
        device.ctrl_transfer(_TYPE_OUT, _REQ_SET_TILT, value, 0, [])
    except Exception as exc:  # noqa: BLE001
        _drop_device(f"Commande moteur impossible ({exc})")
        return False
    return True


def focus(head_position: tuple[float, float], now: float | None = None) -> None:
    """Rapproche la tete de TARGET_HEAD_Y en inclinant la Kinect.

    `head_position` est en coordonnees image normalisees [0,1] (convention MediaPipe,
    origine en haut a gauche) : c'est ce que fournit pose.PoseLandmark, pas des pixels.
    """
    if _state.unavailable:
        return

    _, y = head_position
    # Hors image : detection aberrante, on ne pilote pas le moteur dessus.
    if not 0.0 <= y <= 1.0:
        return

    now = time.monotonic() if now is None else now

    if _state.smoothed_y is None:
        _state.smoothed_y = y
    else:
        _state.smoothed_y += SMOOTHING * (y - _state.smoothed_y)

    if now - _state.last_command_at < COMMAND_INTERVAL_S:
        return

    error = TARGET_HEAD_Y - _state.smoothed_y
    if abs(error) < DEADZONE:
        return

    # La tete est plus bas que la cible (error < 0) => la camera vise trop haut, il faut la
    # baisser. Inversement une tete trop haute dans l'image demande de monter.
    step = max(-MAX_STEP_DEG, min(MAX_STEP_DEG, GAIN_DEG * error))
    if abs(step) < MIN_STEP_DEG:
        step = MIN_STEP_DEG if step > 0 else -MIN_STEP_DEG

    # On part de la derniere consigne et non de l'angle mesure : pendant que le moteur est
    # encore en mouvement, la mesure est en retard et cumuler dessus sur-corrige.
    base = _state.target_deg
    if base is None:
        state = read_state(now)
        if state is None:
            return
        base, status = state
        if status == TILT_STATUS_MOVING:
            return  # mouvement en cours (ordre exterieur, recalage) : on le laisse finir

    target = max(TILT_MIN_DEG, min(TILT_MAX_DEG, base + step))
    if target == _state.target_deg:
        # Deja en butee dans cette direction : inutile de reemettre l'ordre.
        return

    if not move_kinect(target, now):
        return
    _state.target_deg = target
    _state.last_command_at = now


def reset() -> None:
    """Oublie l'etat de suivi (entre deux joueurs) sans lacher le moteur."""
    _state.smoothed_y = None
    _state.last_command_at = 0.0
    _state.target_deg = None
