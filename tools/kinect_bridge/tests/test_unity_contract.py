"""Test de contrat entre protocol.py (Python) et KinectInputSource.cs (Unity).

Les offsets de lecture sont écrits en dur des deux côtés. Ce test rejoue EXACTEMENT la
logique de parsing du C# (mêmes offsets, mêmes types, little-endian) sur un paquet produit
par `protocol.pack()`. Si quelqu'un modifie le format côté Python sans mettre à jour le C#,
ce test casse et indique quoi corriger — sinon la divergence ne se verrait qu'au runtime
dans Unity, sous forme de valeurs aberrantes difficiles à relier à leur cause.
"""
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge import protocol

CS_FILE = Path(__file__).resolve().parents[3] / "Assets" / "Scripts" / "KinectInput" / "KinectInputSource.cs"

# Offsets tels qu'ils sont codés dans KinectInputSource.ReceiveLoop().
CS_OFFSETS = {
    "magic": (0, 4),
    "version": (4, 1),
    "flags": (5, 1),
    "seq": (6, 4),
    "timestamp": (10, 8),
    "distance": (18, 4),
    "lean": (22, 4),
    "lift": (26, 4),
    "throttle": (30, 4),
    "glide": (34, 4),
    "confidence": (38, 4),
    "jointCount": (42, 1),
}
CS_HEADER_SIZE = 43
CS_JOINT_SIZE = 16
CS_JOINT_COUNT = 9


def test_cs_constants_match_python():
    assert CS_HEADER_SIZE == protocol.HEADER_SIZE
    assert CS_JOINT_SIZE == protocol.JOINT_SIZE
    assert CS_JOINT_COUNT == protocol.JOINT_COUNT
    assert CS_HEADER_SIZE + CS_JOINT_COUNT * CS_JOINT_SIZE == protocol.PACKET_SIZE


def test_cs_offsets_decode_a_real_packet():
    """Décode un paquet réel avec les offsets du C#, et compare aux valeurs d'origine."""
    joints = tuple(
        protocol.Joint(x=0.1 * i, y=0.2 * i, z=-0.05 * i, confidence=0.5 + 0.05 * i)
        for i in range(protocol.JOINT_COUNT)
    )
    src = protocol.SkeletonPacket(
        seq=123456, timestamp=1788859689.25,
        player_present=True, in_zone=True,
        distance=2.47, lean=-0.42, lift=0.75, throttle=0.31, glide=0.9,
        confidence=0.88, joints=joints,
    )
    data = protocol.pack(src)
    assert len(data) == protocol.PACKET_SIZE

    def u8(o): return data[o]
    def u32(o): return struct.unpack_from("<I", data, o)[0]
    def f32(o): return struct.unpack_from("<f", data, o)[0]
    def f64(o): return struct.unpack_from("<d", data, o)[0]

    assert data[0:4] == b"KBRD", "magic lu au mauvais offset côté C#"
    assert u8(CS_OFFSETS["version"][0]) == protocol.VERSION

    flags = u8(CS_OFFSETS["flags"][0])
    assert flags & 0x01, "bit0 = playerPresent"
    assert flags & 0x02, "bit1 = inZone"

    assert u32(CS_OFFSETS["seq"][0]) == 123456
    assert abs(f64(CS_OFFSETS["timestamp"][0]) - 1788859689.25) < 1e-6
    assert abs(f32(CS_OFFSETS["distance"][0]) - 2.47) < 1e-5
    assert abs(f32(CS_OFFSETS["lean"][0]) - (-0.42)) < 1e-5
    assert abs(f32(CS_OFFSETS["lift"][0]) - 0.75) < 1e-5
    assert abs(f32(CS_OFFSETS["throttle"][0]) - 0.31) < 1e-5
    assert abs(f32(CS_OFFSETS["glide"][0]) - 0.9) < 1e-5
    assert abs(f32(CS_OFFSETS["confidence"][0]) - 0.88) < 1e-5
    assert u8(CS_OFFSETS["jointCount"][0]) == protocol.JOINT_COUNT

    for i in range(CS_JOINT_COUNT):
        o = CS_HEADER_SIZE + i * CS_JOINT_SIZE
        assert abs(f32(o) - joints[i].x) < 1e-5, f"joint {i} x"
        assert abs(f32(o + 4) - joints[i].y) < 1e-5, f"joint {i} y"
        assert abs(f32(o + 8) - joints[i].z) < 1e-5, f"joint {i} z"
        assert abs(f32(o + 12) - joints[i].confidence) < 1e-5, f"joint {i} confidence"


def test_cs_file_declares_the_same_constants():
    """Garde-fou textuel : les constantes du C# doivent rester alignées sur protocol.py."""
    if not CS_FILE.exists():
        print(f"  (skip: {CS_FILE} introuvable)")
        return
    src = CS_FILE.read_text(encoding="utf-8")
    for literal in (
        f"HeaderSize = {protocol.HEADER_SIZE}",
        f"JointCount = {protocol.JOINT_COUNT}",
        f"JointSize = {protocol.JOINT_SIZE}",
        f"ProtocolVersion = {protocol.VERSION}",
    ):
        assert literal in src, f"KinectInputSource.cs ne déclare plus `{literal}`"

    # L'ordre des articulations doit être identique des deux côtés (le squelette de debug en dépend).
    for name in protocol.JOINT_NAMES:
        assert f'"{name}"' in src, f"articulation {name} absente de KinectInputSource.JointNames"


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
