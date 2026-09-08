"""Enregistrement / relecture de sessions (implementation_plan.md section 3.4).

Une seule Kinect pour l'équipe : ce module permet à quiconque de tester toute
l'intégration Unity avec des données réelles, à la cadence d'origine, sans matériel.

Format fichier .kbr : en-tête `KBRC` + version, puis une suite de
(float64 timestamp_relatif, uint32 taille, bytes paquet_brut).
"""
from __future__ import annotations

import struct
import time
from pathlib import Path
from typing import Iterator

FILE_MAGIC = b"KBRC"
FILE_VERSION = 1
_RECORD_HEADER_FORMAT = "<dI"  # timestamp relatif (s depuis le début) + taille du paquet
_RECORD_HEADER_SIZE = struct.calcsize(_RECORD_HEADER_FORMAT)


class SessionRecorder:
    """Écrit les paquets bruts (déjà sérialisés par protocol.pack) au fil de l'eau."""

    def __init__(self, path: str | Path) -> None:
        self._file = open(path, "wb")
        self._file.write(FILE_MAGIC)
        self._file.write(struct.pack("<B", FILE_VERSION))
        self._start_time: float | None = None

    def write(self, raw_packet: bytes, now: float | None = None) -> None:
        now = now if now is not None else time.time()
        if self._start_time is None:
            self._start_time = now
        rel_t = now - self._start_time
        self._file.write(struct.pack(_RECORD_HEADER_FORMAT, rel_t, len(raw_packet)))
        self._file.write(raw_packet)

    def close(self) -> None:
        self._file.close()

    def __enter__(self) -> "SessionRecorder":
        return self

    def __exit__(self, *exc) -> None:
        self.close()


def replay(path: str | Path) -> Iterator[tuple[float, bytes]]:
    """Générateur (timestamp_relatif, paquet_brut) dans l'ordre d'enregistrement.

    Ne gère pas le rythme de lecture : voir `replay_realtime` pour rejouer à la cadence
    d'origine (ce que consomme kinect_skeleton_bridge.py --replay).
    """
    with open(path, "rb") as f:
        magic = f.read(4)
        if magic != FILE_MAGIC:
            raise ValueError(f"Fichier de session invalide : magic {magic!r} (attendu {FILE_MAGIC!r})")
        (version,) = struct.unpack("<B", f.read(1))
        if version != FILE_VERSION:
            raise ValueError(f"Version de fichier de session non supportée : {version}")

        while True:
            header = f.read(_RECORD_HEADER_SIZE)
            if len(header) < _RECORD_HEADER_SIZE:
                break
            rel_t, size = struct.unpack(_RECORD_HEADER_FORMAT, header)
            data = f.read(size)
            if len(data) < size:
                raise ValueError("Fichier de session tronqué")
            yield rel_t, data


def replay_realtime(path: str | Path, loop: bool = True) -> Iterator[bytes]:
    """Rejoue les paquets à la cadence d'origine (respecte les timestamps relatifs)."""
    while True:
        t0 = time.time()
        for rel_t, data in replay(path):
            target = t0 + rel_t
            delay = target - time.time()
            if delay > 0:
                time.sleep(delay)
            yield data
        if not loop:
            return
