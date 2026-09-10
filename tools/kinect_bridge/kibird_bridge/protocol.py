"""Contrat reseau UDP Kinect -> Unity.

Format binaire fixe, little-endian : un paquet = un etat complet.
En-tete (43 octets) + 9 articulations x 16 octets = 187 octets.
Doit rester synchronise avec Assets/Scripts/KinectInput/KinectInputSource.cs.
"""
from __future__ import annotations

import struct
import time
from dataclasses import dataclass, field

MAGIC = b"KBRD"
VERSION = 1

JOINT_NAMES = (
    "NOSE",
    "L_SHOULDER",
    "R_SHOULDER",
    "L_ELBOW",
    "R_ELBOW",
    "L_WRIST",
    "R_WRIST",
    "L_HIP",
    "R_HIP",
)
JOINT_COUNT = len(JOINT_NAMES)

FLAG_PLAYER_PRESENT = 1 << 0
FLAG_IN_ZONE = 1 << 1

HEADER_FORMAT = "<4sBBIdffffffB"
HEADER_SIZE = struct.calcsize(HEADER_FORMAT)
JOINT_FORMAT = "<ffff"
JOINT_SIZE = struct.calcsize(JOINT_FORMAT)
PACKET_SIZE = HEADER_SIZE + JOINT_COUNT * JOINT_SIZE

assert HEADER_SIZE == 43, f"HEADER_SIZE attendu 43, obtenu {HEADER_SIZE}"
assert PACKET_SIZE == 187, f"PACKET_SIZE attendu 187, obtenu {PACKET_SIZE}"


@dataclass
class Joint:
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0
    confidence: float = 0.0


@dataclass
class SkeletonPacket:
    seq: int = 0
    timestamp: float = field(default_factory=time.time)
    player_present: bool = False
    in_zone: bool = False
    distance: float = 0.0
    lean: float = 0.0
    lift: float = 0.0
    throttle: float = 0.0
    glide: float = 0.0
    confidence: float = 0.0
    joints: tuple[Joint, ...] = field(
        default_factory=lambda: tuple(Joint() for _ in range(JOINT_COUNT))
    )

    def __post_init__(self) -> None:
        if len(self.joints) != JOINT_COUNT:
            raise ValueError(
                f"SkeletonPacket attend {JOINT_COUNT} articulations, recu {len(self.joints)}"
            )


def pack(packet: SkeletonPacket) -> bytes:
    flags = 0
    if packet.player_present:
        flags |= FLAG_PLAYER_PRESENT
    if packet.in_zone:
        flags |= FLAG_IN_ZONE

    header = struct.pack(
        HEADER_FORMAT,
        MAGIC,
        VERSION,
        flags,
        packet.seq & 0xFFFFFFFF,
        packet.timestamp,
        packet.distance,
        packet.lean,
        packet.lift,
        packet.throttle,
        packet.glide,
        packet.confidence,
        JOINT_COUNT,
    )

    body = bytearray(JOINT_COUNT * JOINT_SIZE)
    offset = 0
    for j in packet.joints:
        struct.pack_into(JOINT_FORMAT, body, offset, j.x, j.y, j.z, j.confidence)
        offset += JOINT_SIZE

    return header + bytes(body)


def unpack(data: bytes) -> SkeletonPacket:
    """Leve ValueError si le paquet est invalide."""
    if len(data) < HEADER_SIZE:
        raise ValueError(f"Paquet trop court : {len(data)} < {HEADER_SIZE} octets")

    (
        magic,
        version,
        flags,
        seq,
        timestamp,
        distance,
        lean,
        lift,
        throttle,
        glide,
        confidence,
        joint_count,
    ) = struct.unpack(HEADER_FORMAT, data[:HEADER_SIZE])

    if magic != MAGIC:
        raise ValueError(f"Magic invalide : {magic!r} (attendu {MAGIC!r})")
    if version != VERSION:
        raise ValueError(f"Version de protocole non supportee : {version}")

    expected_size = HEADER_SIZE + joint_count * JOINT_SIZE
    if len(data) < expected_size:
        raise ValueError(
            f"Corps du paquet incomplet : {len(data)} < {expected_size} octets attendus"
        )

    joints = []
    offset = HEADER_SIZE
    for _ in range(joint_count):
        x, y, z, conf = struct.unpack(JOINT_FORMAT, data[offset : offset + JOINT_SIZE])
        joints.append(Joint(x, y, z, conf))
        offset += JOINT_SIZE

    return SkeletonPacket(
        seq=seq,
        timestamp=timestamp,
        player_present=bool(flags & FLAG_PLAYER_PRESENT),
        in_zone=bool(flags & FLAG_IN_ZONE),
        distance=distance,
        lean=lean,
        lift=lift,
        throttle=throttle,
        glide=glide,
        confidence=confidence,
        joints=tuple(joints),
    )
