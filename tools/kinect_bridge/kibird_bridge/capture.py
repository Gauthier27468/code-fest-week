"""Capture Kinect 1414 via libfreenect : flux RGB + profondeur alignée.

La distance du joueur est mesurée sur la vraie profondeur Kinect (pas sur l'échelle
apparente du corps estimée par MediaPipe) : voir implementation_plan.md section 3.1.
Le mode registered aligne la depth map sur le repère de l'image RGB, ce qui est
indispensable puisque les caméras RGB et IR sont physiquement décalées sur la Kinect.
"""
from __future__ import annotations

import glob
import time
from dataclasses import dataclass
from pathlib import Path

import freenect
import numpy as np


class KinectUnavailableError(RuntimeError):
    """La Kinect n'a pas pu être ouverte. Le message porte le diagnostic et la marche à suivre."""


def _diagnose_failure() -> str:
    """Construit un message actionnable au lieu du `TypeError: cannot unpack NoneType` de freenect.

    Le cas de loin le plus fréquent est le module noyau `gspca_kinect`, chargé automatiquement,
    qui réserve la caméra et fait échouer libfreenect avec `LIBUSB_ERROR_BUSY`.
    """
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
        lines.append("  -> Aucun périphérique Microsoft détecté : vérifie le câble USB et "
                     "l'alimentation secteur de la Kinect (elle en a besoin, l'USB ne suffit pas).")
        return "\n".join(lines)

    lines.append("  La Kinect est bien branchée (périphérique USB Microsoft détecté).")
    if Path("/sys/module/gspca_kinect").exists():
        lines.append("  -> CAUSE PROBABLE : le module noyau `gspca_kinect` est chargé et réserve")
        lines.append("     la caméra (LIBUSB_ERROR_BUSY). Corrige avec :")
        lines.append("         sudo rmmod gspca_kinect")
        lines.append("     puis, pour que ça survive au redémarrage :")
        lines.append("         echo 'blacklist gspca_kinect' | sudo tee /etc/modprobe.d/kibird-kinect.conf")
    else:
        lines.append("  -> Vérifie qu'aucun autre process n'utilise déjà la Kinect "
                     "(un bridge lancé dans un autre terminal ?).")
    return "\n".join(lines)


@dataclass
class Frame:
    rgb: np.ndarray  # (480, 640, 3) uint8
    depth_mm: np.ndarray  # (480, 640) uint16, aligné sur rgb, 0 = pixel invalide (trou IR)
    timestamp: float  # time.time() à la capture (horloge murale) — PAS le tick interne freenect


class KinectCapture:
    """Wrapper fin autour de freenect. Une instance = un accès exclusif au device 0."""

    def __init__(self, use_registered_depth: bool = True) -> None:
        self._depth_format = (
            freenect.DEPTH_REGISTERED if use_registered_depth else freenect.DEPTH_MM
        )

    def read(self) -> Frame:
        """Bloque jusqu'à la prochaine frame disponible (synchrone, cadence Kinect ~30 Hz).

        ⚠️ freenect renvoie un timestamp qui est un compteur interne au driver (pas une heure
        epoch) : on l'ignore et on horodate nous-mêmes avec `time.time()`, seul moyen de
        mesurer la latence bout-en-bout attendue par le contrat réseau (implementation_plan.md §2,
        critère de réussite < 150 ms).
        """
        video = freenect.sync_get_video()
        if video is None:
            raise KinectUnavailableError(_diagnose_failure())
        depth = freenect.sync_get_depth(format=self._depth_format)
        if depth is None:
            raise KinectUnavailableError(_diagnose_failure())
        return Frame(rgb=video[0], depth_mm=depth[0], timestamp=time.time())

    def median_depth_at(self, depth_mm: np.ndarray, px: int, py: int, window: int = 20) -> float:
        """Médiane de la profondeur (mètres) dans une fenêtre window x window centrée sur (px, py).

        Les pixels à 0 (trous IR, hors de portée) sont ignorés. Retourne 0.0 si la fenêtre
        ne contient aucun pixel valide (typiquement : joueur hors de portée du capteur IR).
        """
        h, w = depth_mm.shape
        half = window // 2
        x0, x1 = max(0, px - half), min(w, px + half)
        y0, y1 = max(0, py - half), min(h, py + half)
        region = depth_mm[y0:y1, x0:x1]
        valid = region[region > 0]
        if valid.size == 0:
            return 0.0
        return float(np.median(valid)) / 1000.0  # mm -> m

    def close(self) -> None:
        freenect.sync_stop()
