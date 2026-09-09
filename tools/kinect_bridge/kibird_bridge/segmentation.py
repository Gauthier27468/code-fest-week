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


def foreground_mask(
    depth_mm: np.ndarray,
    max_depth_m: float = DEFAULT_MAX_DEPTH_M,
    close_px: int = DEFAULT_CLOSE_PX,
    dilate_px: int = DEFAULT_DILATE_PX,
) -> np.ndarray:
    """Masque booléen (H, W) des pixels à conserver : profondeur valide et <= max_depth_m."""
    max_mm = max_depth_m * 1000.0
    mask = ((depth_mm > 0) & (depth_mm <= max_mm)).astype(np.uint8)

    if close_px > 1:
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (close_px, close_px))
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    if dilate_px > 1:
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (dilate_px, dilate_px))
        mask = cv2.dilate(mask, kernel)
    return mask.astype(bool)


def remove_background(
    rgb: np.ndarray,
    depth_mm: np.ndarray,
    max_depth_m: float = DEFAULT_MAX_DEPTH_M,
    fill: int = 0,
) -> np.ndarray:
    """Renvoie une copie de `rgb` où tout ce qui est au-delà de `max_depth_m` vaut `fill`.

    Renvoie `rgb` tel quel (aucune copie) si les dimensions ne concordent pas ou si le masque
    ne retient quasiment rien : le filtre ne doit jamais être la cause d'une perte de joueur.
    """
    if depth_mm.shape[:2] != rgb.shape[:2]:
        return rgb

    mask = foreground_mask(depth_mm, max_depth_m=max_depth_m)
    if mask.mean() < MIN_FOREGROUND_RATIO:
        return rgb

    filtered = np.empty_like(rgb)
    filtered[:] = fill
    np.copyto(filtered, rgb, where=mask[:, :, None])
    return filtered
