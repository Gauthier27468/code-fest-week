"""Suppression du fond par profondeur IR, avant l'envoi de l'image à MediaPipe.

Le capteur IR de la Kinect donne une profondeur par pixel, alignée sur l'image RGB en mode
registered (cf. capture.py). Tout ce qui est au-delà de la zone de jeu (4 m, AGENTS.md) est
donc identifiable et peut être noirci avant la détection de pose : MediaPipe ne voit plus les
passants du fond, ce qui supprime la principale source de faux squelettes en JPO (un visiteur
qui traverse derrière le joueur) et réduit le travail de `_select_front_skeleton`.

⚠️ N'a de sens QU'EN profondeur registered (ce que fournit `capture.py`). Avec une depth map
non alignée sur la RGB, le masque ne correspond pas aux pixels et découperait le joueur.
"""
from __future__ import annotations

import cv2
import numpy as np

DEFAULT_MAX_DEPTH_M = 4.0
# Les trous IR (depth == 0) sont fréquents SUR le joueur : bords du corps, cheveux, tissus
# absorbants. Les fermer évite de mitrailler la silhouette de pixels noirs, ce qui ferait
# chuter la confiance des landmarks MediaPipe.
DEFAULT_CLOSE_PX = 11
# Marge de sécurité : la depth registered est légèrement décalée sur les contours (parallaxe
# résiduelle entre caméras RGB et IR). Sans dilatation on rogne quelques pixels du joueur —
# typiquement les poignets, précisément les articulations qui portent le geste de battement.
DEFAULT_DILATE_PX = 9
# En dessous de cette fraction de pixels conservés, on considère le masque non exploitable
# (Kinect qui renvoie une depth vide, joueur hors de portée IR) et on laisse l'image intacte :
# mieux vaut un fond bruité qu'une image entièrement noire où plus personne n'est détecté.
MIN_FOREGROUND_RATIO = 0.01
# Un blob plus petit que ça n'est pas une personne (bras qui dépasse, reflet IR, pied de
# micro) : le garder comme candidat "le plus proche" ferait éliminer le vrai joueur.
# Repère : un joueur à 4 m occupe ~15 000 px sur 640x480, à 2 m ~75 000 px.
MIN_BLOB_AREA_PX = 2500


def _nearest_blob(mask: np.ndarray, depth_mm: np.ndarray) -> np.ndarray:
    """Ne garde que la composante connexe la plus proche de la Kinect.

    Sans ça, deux visiteurs tous les deux à moins de 4 m survivent au seuil de profondeur et
    MediaPipe doit chercher plusieurs poses (`--num-poses > 1`), ce qui double son temps
    d'inférence (mesuré : 31 ms -> 56 ms par frame). En ne laissant qu'une personne dans
    l'image, `--num-poses 1` suffit et le tracker interne de MediaPipe garde sa fast-path.

    ⚠️ Limite connue : si le SOL est visible dans la zone < 4 m, il forme un blob qui peut
    relier deux personnes en une seule composante — le filtre ne les sépare alors plus.
    Incliner légèrement la Kinect vers le haut, ou repasser à `--num-poses 2`, si le cas se
    présente à l'installation.
    """
    count, labels, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    best_label, best_depth = 0, None
    for i in range(1, count):
        if stats[i, cv2.CC_STAT_AREA] < MIN_BLOB_AREA_PX:
            continue
        # Médiane calculée sur la bounding box du blob, pas sur l'image entière : 3x moins
        # de pixels à parcourir, ce qui garde cette passe sous 2 ms.
        x, y = stats[i, cv2.CC_STAT_LEFT], stats[i, cv2.CC_STAT_TOP]
        w, h = stats[i, cv2.CC_STAT_WIDTH], stats[i, cv2.CC_STAT_HEIGHT]
        blob_depth = depth_mm[y:y + h, x:x + w][labels[y:y + h, x:x + w] == i]
        blob_depth = blob_depth[blob_depth > 0]
        if blob_depth.size == 0:
            continue
        median = float(np.median(blob_depth))
        if best_depth is None or median < best_depth:
            best_label, best_depth = i, median

    if best_depth is None:
        return mask  # aucun blob exploitable : on garde le masque brut plutôt que rien
    return (labels == best_label).astype(np.uint8)


def foreground_mask(
    depth_mm: np.ndarray,
    max_depth_m: float = DEFAULT_MAX_DEPTH_M,
    close_px: int = DEFAULT_CLOSE_PX,
    dilate_px: int = DEFAULT_DILATE_PX,
    isolate_nearest: bool = True,
) -> np.ndarray:
    """Masque uint8 (H, W), 1 = pixel conservé : profondeur valide et <= max_depth_m.

    uint8 et non bool : c'est le format attendu par `cv2.bitwise_and`, seule façon d'appliquer
    le masque en 0.15 ms au lieu de 2.8 ms avec `np.copyto(where=...)`.

    `isolate_nearest` ne garde ensuite que la personne la plus en avant (cf. `_nearest_blob`).
    """
    max_mm = max_depth_m * 1000.0
    mask = ((depth_mm > 0) & (depth_mm <= max_mm)).astype(np.uint8)

    if close_px > 1:
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (close_px, close_px))
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    if dilate_px > 1:
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (dilate_px, dilate_px))
        mask = cv2.dilate(mask, kernel)
    if isolate_nearest:
        mask = _nearest_blob(mask, depth_mm)
    return mask


def remove_background(
    rgb: np.ndarray,
    depth_mm: np.ndarray,
    max_depth_m: float = DEFAULT_MAX_DEPTH_M,
    isolate_nearest: bool = True,
) -> np.ndarray:
    """Renvoie une copie de `rgb` où tout ce qui est au-delà de `max_depth_m` est noirci.

    Renvoie `rgb` tel quel (aucune copie) si les dimensions ne concordent pas ou si le masque
    ne retient quasiment rien : le filtre ne doit jamais être la cause d'une perte de joueur.
    """
    if depth_mm.shape[:2] != rgb.shape[:2]:
        return rgb

    mask = foreground_mask(depth_mm, max_depth_m=max_depth_m, isolate_nearest=isolate_nearest)
    if mask.mean() < MIN_FOREGROUND_RATIO:
        return rgb

    # bitwise_and (SIMD, in-place sur le buffer de sortie) : 0.15 ms contre 2.8 ms pour
    # `np.empty_like` + `np.copyto(where=...)`, sur un budget de 33 ms par frame à 30 Hz.
    return cv2.bitwise_and(rgb, rgb, mask=mask)
