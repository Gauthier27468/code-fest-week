"""Sources video Kinect/webcam exposees sous un contrat commun au bridge."""
from __future__ import annotations

import glob
import math
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np


class CaptureUnavailableError(RuntimeError):
    """La source demandee ne peut pas fournir d'image."""


class KinectUnavailableError(CaptureUnavailableError):
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
    depth_mm: np.ndarray | None  # None pour une webcam sans capteur de profondeur
    timestamp: float  # time.time() a la capture, pas le tick interne freenect


class KinectCapture:
    """Une instance = un acces exclusif au device 0."""

    source_name = "kinect"
    has_depth = True
    has_motor = True

    def __init__(self) -> None:
        # Import differe : le mode webcam peut demarrer meme si libfreenect est indisponible.
        try:
            import freenect
        except ImportError as exc:
            raise KinectUnavailableError(f"libfreenect indisponible: {exc}") from exc
        self._freenect = freenect

    def read(self) -> Frame:
        """Bloque jusqu'a la prochaine frame (synchrone, cadence Kinect ~30 Hz)."""
        video = self._freenect.sync_get_video()
        if video is None:
            raise KinectUnavailableError(_diagnose_failure())
        depth = self._freenect.sync_get_depth(format=self._freenect.DEPTH_REGISTERED)
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

    def distance_for_skeleton(self, skeleton: dict, frame: Frame) -> float:
        px, py = _hip_mid_pixel(skeleton, frame.rgb.shape[1], frame.rgb.shape[0])
        return self.median_depth_at(frame.depth_mm, px, py)

    def close(self) -> None:
        try:
            self._freenect.sync_stop()
        except Exception:
            # La fermeture est best-effort apres une initialisation USB incomplete.
            pass


class WebcamCapture:
    """Capture RGB classique avec estimation monoculaire de la distance."""

    source_name = "webcam"
    has_depth = False
    has_motor = False

    def __init__(
        self,
        device: int = 0,
        width: int = 640,
        height: int = 480,
        fps: float = 30.0,
        horizontal_fov_deg: float = 70.0,
        shoulder_width_m: float = 0.38,
    ) -> None:
        import cv2

        self._cv2 = cv2
        self._device = device
        self._horizontal_fov_deg = horizontal_fov_deg
        self._shoulder_width_m = shoulder_width_m
        self._capture = cv2.VideoCapture(device)
        self._capture.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self._capture.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self._capture.set(cv2.CAP_PROP_FPS, fps)
        if not self._capture.isOpened():
            self._capture.release()
            raise CaptureUnavailableError(f"Impossible d'ouvrir la webcam {device}.")

    def read(self) -> Frame:
        ok, bgr = self._capture.read()
        if not ok or bgr is None:
            raise CaptureUnavailableError(f"La webcam {self._device} ne renvoie aucune image.")
        rgb = self._cv2.cvtColor(bgr, self._cv2.COLOR_BGR2RGB)
        return Frame(rgb=rgb, depth_mm=None, timestamp=time.time())

    def distance_for_skeleton(self, skeleton: dict, frame: Frame) -> float:
        shoulder_width_norm = abs(skeleton["L_SHOULDER"].x - skeleton["R_SHOULDER"].x)
        shoulder_width_px = shoulder_width_norm * frame.rgb.shape[1]
        if shoulder_width_px <= 1e-6:
            return 0.0
        focal_px = (frame.rgb.shape[1] * 0.5) / math.tan(
            math.radians(self._horizontal_fov_deg) * 0.5
        )
        return self._shoulder_width_m * focal_px / shoulder_width_px

    def close(self) -> None:
        self._capture.release()


def _hip_mid_pixel(skeleton: dict, width: int, height: int) -> tuple[int, int]:
    lx, ly = skeleton["L_HIP"].x, skeleton["L_HIP"].y
    rx, ry = skeleton["R_HIP"].x, skeleton["R_HIP"].y
    return int((lx + rx) * 0.5 * width), int((ly + ry) * 0.5 * height)


def kinect_is_connected() -> bool:
    """Detection USB legere, sans ouvrir le flux ni attendre son timeout."""
    for vendor_path in glob.glob("/sys/bus/usb/devices/*/idVendor"):
        try:
            if Path(vendor_path).read_text().strip().lower() != "045e":
                continue
            product_path = Path(vendor_path).with_name("idProduct")
            # Sous-device camera Kinect 360 (02ae) ou moteur (02b0).
            if product_path.read_text().strip().lower() in {"02ae", "02b0"}:
                return True
        except OSError:
            continue
    return False
