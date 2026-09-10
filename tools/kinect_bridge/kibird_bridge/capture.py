"""Capture Kinect 1414 via libfreenect : flux RGB + profondeur alignee sur le repere RGB."""
from __future__ import annotations

import glob
import time
from dataclasses import dataclass
from pathlib import Path

import freenect
import numpy as np


class KinectUnavailableError(RuntimeError):
    """La Kinect n'a pas pu etre ouverte. Le message porte le diagnostic et la marche a suivre."""


def _diagnose_failure() -> str:
    """Message actionnable, a la place du `TypeError: cannot unpack NoneType` de freenect."""
    lines = ["Impossible d'ouvrir la Kinect."]

    plugged = False
    try:
        plugged = any(
            Path(p).read_text().strip() == "045e"
            for p in glob.glob("/sys/bus/usb/devices/*/idVendor")
        )
    except OSError:
        pass

    if not plugged:
        lines.append("  -> Aucun peripherique Microsoft detecte : verifie le cable USB et "
                     "l'alimentation secteur de la Kinect (elle en a besoin, l'USB ne suffit pas).")
        return "\n".join(lines)

    lines.append("  La Kinect est bien branchee (peripherique USB Microsoft detecte).")
    if Path("/sys/module/gspca_kinect").exists():
        lines.append("  -> CAUSE PROBABLE : le module noyau `gspca_kinect` est charge et reserve")
        lines.append("     la camera (LIBUSB_ERROR_BUSY). Corrige avec :")
        lines.append("         sudo rmmod gspca_kinect")
        lines.append("     puis, pour que ca survive au redemarrage :")
        lines.append("         echo 'blacklist gspca_kinect' | sudo tee /etc/modprobe.d/kibird-kinect.conf")
    else:
        lines.append("  -> Verifie qu'aucun autre process n'utilise deja la Kinect "
                     "(un bridge lance dans un autre terminal ?).")
    return "\n".join(lines)


@dataclass
class Frame:
    rgb: np.ndarray  # (480, 640, 3) uint8
    depth_mm: np.ndarray  # (480, 640) uint16, aligne sur rgb, 0 = pixel invalide
    timestamp: float  # time.time() a la capture, pas le tick interne freenect


class KinectCapture:
    """Une instance = un acces exclusif au device 0."""

    def read(self) -> Frame:
        """Bloque jusqu'a la prochaine frame (synchrone, cadence Kinect ~30 Hz)."""
        video = freenect.sync_get_video()
        if video is None:
            raise KinectUnavailableError(_diagnose_failure())
        depth = freenect.sync_get_depth(format=freenect.DEPTH_REGISTERED)
        if depth is None:
            raise KinectUnavailableError(_diagnose_failure())
        return Frame(rgb=video[0], depth_mm=depth[0], timestamp=time.time())

    def median_depth_at(self, depth_mm: np.ndarray, px: int, py: int, window: int = 20) -> float:
        """Mediane de la profondeur (metres) dans une fenetre centree sur (px, py).

        Les pixels a 0 (trous IR, hors de portee) sont ignores. Retourne 0.0 si la fenetre
        ne contient aucun pixel valide.
        """
        h, w = depth_mm.shape
        half = window // 2
        x0, x1 = max(0, px - half), min(w, px + half)
        y0, y1 = max(0, py - half), min(h, py + half)
        region = depth_mm[y0:y1, x0:x1]
        valid = region[region > 0]
        if valid.size == 0:
            return 0.0
        return float(np.median(valid)) / 1000.0
