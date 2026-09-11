"""Tests du contrat réseau : round-trip pack/unpack, tailles fixes, rejet des paquets invalides."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge import protocol


def test_sizes():
    assert protocol.HEADER_SIZE == 43
    assert protocol.PACKET_SIZE == 187
    assert protocol.JOINT_COUNT == 9


def test_roundtrip_full():
    joints = tuple(
        protocol.Joint(x=i * 0.1, y=i * 0.2, z=i * 0.3, confidence=0.9)
        for i in range(protocol.JOINT_COUNT)
    )
    pkt = protocol.SkeletonPacket(
        seq=42,
        timestamp=1234567890.123,
        player_present=True,
        in_zone=True,
        distance=2.5,
        lean=-0.75,
        lift=0.3,
        throttle=0.6,
        glide=0.9,
        confidence=0.85,
        joints=joints,
    )
    data = protocol.pack(pkt)
    assert len(data) == protocol.PACKET_SIZE

    decoded = protocol.unpack(data)
    assert decoded.seq == 42
    assert abs(decoded.timestamp - 1234567890.123) < 1e-6
    assert decoded.player_present is True
    assert decoded.in_zone is True
    assert abs(decoded.distance - 2.5) < 1e-6
    assert abs(decoded.lean - (-0.75)) < 1e-6
    assert abs(decoded.lift - 0.3) < 1e-6
    assert abs(decoded.throttle - 0.6) < 1e-6
    assert abs(decoded.glide - 0.9) < 1e-6
    for orig, dec in zip(joints, decoded.joints):
        assert abs(orig.x - dec.x) < 1e-5
        assert abs(orig.confidence - dec.confidence) < 1e-5


def test_heartbeat_no_player():
    """Paquet émis même sans joueur : le collègue Unity doit distinguer bridge mort / salle vide."""
    pkt = protocol.SkeletonPacket(seq=1, player_present=False, in_zone=False)
    data = protocol.pack(pkt)
    decoded = protocol.unpack(data)
    assert decoded.player_present is False
    assert len(data) == protocol.PACKET_SIZE


def test_rejects_bad_magic():
    pkt = protocol.SkeletonPacket(seq=1)
    data = bytearray(protocol.pack(pkt))
    data[0:4] = b"XXXX"
    try:
        protocol.unpack(bytes(data))
        assert False, "devrait lever ValueError"
    except ValueError as e:
        assert "Magic" in str(e)


def test_rejects_truncated_packet():
    pkt = protocol.SkeletonPacket(seq=1)
    data = protocol.pack(pkt)
    try:
        protocol.unpack(data[:20])
        assert False, "devrait lever ValueError"
    except ValueError as e:
        assert "court" in str(e)


def test_rejects_wrong_joint_count():
    try:
        protocol.SkeletonPacket(seq=1, joints=(protocol.Joint(),) * 3)
        assert False, "devrait lever ValueError"
    except ValueError:
        pass


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
