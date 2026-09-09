"""Filtre de fond par profondeur : masque, tolérance aux trous IR, et non-régression MediaPipe.

Ne dépend PAS de freenect (le module `segmentation` est volontairement séparé de `capture`) :
ces tests tournent donc sans Kinect branchée.
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge.segmentation import foreground_mask, remove_background  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
MODEL = ROOT / "models" / "pose_landmarker_lite.task"
IMAGE = ROOT / "tests" / "fixtures" / "person.jpg"


def _depth(fill_mm: int, shape=(480, 640)) -> np.ndarray:
    return np.full(shape, fill_mm, dtype=np.uint16)


def test_keeps_near_pixels_and_drops_far_ones():
    depth = _depth(5000)  # 5 m : hors zone
    depth[100:300, 200:400] = 2000  # 2 m : le joueur
    mask = foreground_mask(depth, max_depth_m=4.0, close_px=0, dilate_px=0)
    assert mask[200, 300]
    assert not mask[10, 10]


def test_ir_holes_inside_player_are_filled():
    depth = _depth(5000)
    depth[100:300, 200:400] = 2000
    depth[190:196, 290:296] = 0  # trou IR au milieu du joueur
    assert not foreground_mask(depth, close_px=0, dilate_px=0)[193, 293]
    assert foreground_mask(depth)[193, 293], "la fermeture morphologique doit boucher le trou"


def test_background_pixels_are_blacked_out():
    rgb = np.full((480, 640, 3), 200, dtype=np.uint8)
    depth = _depth(5000)
    depth[100:300, 200:400] = 2000
    out = remove_background(rgb, depth, max_depth_m=4.0)
    assert out.dtype == np.uint8 and out.shape == rgb.shape
    assert tuple(out[200, 300]) == (200, 200, 200)
    assert tuple(out[10, 10]) == (0, 0, 0)
    assert tuple(rgb[10, 10]) == (200, 200, 200), "l'image source ne doit pas être modifiée"


def test_empty_depth_leaves_image_untouched():
    """Fail-open : une depth vide (hors portée IR) ne doit jamais produire une image noire."""
    rgb = np.full((480, 640, 3), 200, dtype=np.uint8)
    assert remove_background(rgb, _depth(0)) is rgb
    assert remove_background(rgb, _depth(9000)) is rgb


def test_mismatched_shape_is_ignored():
    rgb = np.full((480, 640, 3), 200, dtype=np.uint8)
    assert remove_background(rgb, _depth(2000, shape=(240, 320))) is rgb


def test_pose_still_detected_after_filtering():
    """Un fond noirci ne doit pas dégrader la détection sur une vraie image."""
    if not (MODEL.exists() and IMAGE.exists()):
        print("  (skip: modèle ou image de test absent)")
        return

    import cv2

    from kibird_bridge.pose import PoseEstimator

    rgb = cv2.cvtColor(cv2.imread(str(IMAGE)), cv2.COLOR_BGR2RGB)
    h, w = rgb.shape[:2]
    # Depth synthétique : la moitié centrale de l'image est "le joueur" à 2 m, le reste à 6 m.
    depth = np.full((h, w), 6000, dtype=np.uint16)
    depth[:, w // 4: 3 * w // 4] = 2000
    filtered = remove_background(rgb, depth, max_depth_m=4.0)
    assert filtered[0, 0].sum() == 0

    estimator = PoseEstimator(MODEL, num_poses=3)
    try:
        skeletons = estimator.detect(filtered)
        assert skeletons, "aucune pose détectée après suppression du fond"
        sk = skeletons[0]
        assert sk["NOSE"].y < sk["L_SHOULDER"].y < sk["L_HIP"].y
    finally:
        estimator.close()


if __name__ == "__main__":
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            fn()
            print(f"OK {name}")
