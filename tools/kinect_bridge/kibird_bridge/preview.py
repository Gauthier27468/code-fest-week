"""Fenêtre de retour caméra avec overlay MediaPipe (option `--preview` du bridge).

Sert au réglage sur site : voir en une image ce que MediaPipe voit réellement (squelettes
détectés, lequel est verrouillé, effet du filtre de fond IR) et ce que le bridge en déduit
(distance, zone, commandes). Sans ça, le diagnostic d'un geste qui ne passe pas se fait à
l'aveugle depuis la ligne de statut console.

⚠️ Option de debug uniquement : `imshow` coûte quelques millisecondes par frame et ouvre une
fenêtre par-dessus le jeu. Ne pas l'activer pendant une JPO.

⚠️ Nécessite un OpenCV avec HighGUI, donc le paquet `opencv-python` et NON
`opencv-python-headless` (voir requirements.txt) : le wheel headless est compilé sans support
de fenêtre et `imshow` y lève une `cv2.error`. Le message d'erreur ci-dessous le dit
explicitement plutôt que de laisser remonter un traceback OpenCV illisible.
"""
from __future__ import annotations

import cv2
import numpy as np

# Segments dessinés entre les 9 articulations exposées par pose.py (buste + bras).
_EDGES = (
    ("L_SHOULDER", "R_SHOULDER"),
    ("L_SHOULDER", "L_ELBOW"),
    ("L_ELBOW", "L_WRIST"),
    ("R_SHOULDER", "R_ELBOW"),
    ("R_ELBOW", "R_WRIST"),
    ("L_SHOULDER", "L_HIP"),
    ("R_SHOULDER", "R_HIP"),
    ("L_HIP", "R_HIP"),
)

# Couleurs BGR (OpenCV), pas RGB.
_COLOR_LOCKED = (80, 255, 80)      # squelette suivi
_COLOR_OTHER = (140, 140, 140)     # détections ignorées (intrusions, passants)
_COLOR_TEXT = (255, 255, 255)
_COLOR_OK = (80, 255, 80)
_COLOR_WARN = (60, 190, 255)
_COLOR_BAR_BG = (60, 60, 60)

_QUIT_KEYS = (ord("q"), 27)  # q, Échap
_TOGGLE_SOURCE_KEY = ord("f")


class PreviewClosed(Exception):
    """Levée quand l'utilisateur ferme la fenêtre (`q` / Échap) : arrêt propre du bridge."""


class PreviewWindow:
    """Fenêtre unique réutilisée à chaque frame. Instancier une seule fois par process."""

    def __init__(self, window_name: str = "KiBird - preview", scale: float = 1.0) -> None:
        self.window_name = window_name
        self.scale = scale
        # False = image brute caméra, True = image réellement envoyée à MediaPipe (fond filtré).
        # Basculable à chaud avec `f` : c'est le seul moyen de vérifier visuellement que
        # --bg-max-distance est bien réglé pour la salle.
        self.show_pose_input = True
        self._window_created = False

    def _ensure_window(self) -> None:
        if self._window_created:
            return
        try:
            cv2.namedWindow(self.window_name, cv2.WINDOW_NORMAL)
        except cv2.error as exc:
            raise RuntimeError(
                "OpenCV est compilé sans support de fenêtre (paquet opencv-python-headless).\n"
                "  --preview a besoin de HighGUI. Dans le venv du bridge :\n"
                "    uv pip install --python .venv/bin/python opencv-python\n"
                f"  (erreur OpenCV : {exc})"
            ) from exc
        self._window_created = True

    def show(
        self,
        raw_rgb: np.ndarray,
        pose_input_rgb: np.ndarray,
        skeletons: list[dict],
        locked_skeleton: dict | None,
        *,
        distance: float,
        in_zone: bool,
        player_present: bool,
        calibrated: bool,
        gesture_out,
        hz: float,
    ) -> None:
        """Dessine une frame et traite les touches. Lève `PreviewClosed` sur `q`/Échap."""
        self._ensure_window()

        source = pose_input_rgb if self.show_pose_input else raw_rgb
        canvas = cv2.cvtColor(source, cv2.COLOR_RGB2BGR)
        if self.scale != 1.0:
            canvas = cv2.resize(canvas, None, fx=self.scale, fy=self.scale, interpolation=cv2.INTER_AREA)
        height, width = canvas.shape[:2]

        for skeleton in skeletons:
            # Comparaison par identité : `_select_front_skeleton` renvoie l'un des dicts de
            # la liste tel quel, pas une copie.
            is_locked = locked_skeleton is not None and skeleton is locked_skeleton
            _draw_skeleton(canvas, skeleton, width, height, is_locked)

        _draw_hud(
            canvas,
            distance=distance,
            in_zone=in_zone,
            player_present=player_present,
            calibrated=calibrated,
            gesture_out=gesture_out,
            hz=hz,
            skeleton_count=len(skeletons),
            source_label="fond filtre (entree MediaPipe)" if self.show_pose_input else "camera brute",
        )

        cv2.imshow(self.window_name, canvas)
        key = cv2.waitKey(1) & 0xFF
        if key in _QUIT_KEYS:
            raise PreviewClosed
        if key == _TOGGLE_SOURCE_KEY:
            self.show_pose_input = not self.show_pose_input

    def close(self) -> None:
        if self._window_created:
            cv2.destroyWindow(self.window_name)
            self._window_created = False


def _draw_skeleton(canvas: np.ndarray, skeleton: dict, width: int, height: int, is_locked: bool) -> None:
    color = _COLOR_LOCKED if is_locked else _COLOR_OTHER
    thickness = 3 if is_locked else 1

    def px(name: str) -> tuple[int, int]:
        lm = skeleton[name]
        return int(lm.x * width), int(lm.y * height)

    for a, b in _EDGES:
        if a in skeleton and b in skeleton:
            cv2.line(canvas, px(a), px(b), color, thickness, cv2.LINE_AA)

    for name, lm in skeleton.items():
        # Rayon proportionnel à la confiance : un point qui rétrécit signale un landmark
        # incertain, cause fréquente d'un geste qui « saute » sans raison apparente.
        radius = 3 + int(4 * max(0.0, min(1.0, lm.visibility)))
        cv2.circle(canvas, px(name), radius, color, -1, cv2.LINE_AA)

    if is_locked and "NOSE" in skeleton:
        nx, ny = px("NOSE")
        _text(canvas, "VERROUILLE", (nx - 45, ny - 18), _COLOR_LOCKED, 0.5)


def _draw_hud(
    canvas: np.ndarray,
    *,
    distance: float,
    in_zone: bool,
    player_present: bool,
    calibrated: bool,
    gesture_out,
    hz: float,
    skeleton_count: int,
    source_label: str,
) -> None:
    lines = [
        (f"{hz:4.1f} Hz   {skeleton_count} squelette(s)   [{source_label}]", _COLOR_TEXT),
        (
            f"joueur={'OUI' if player_present else 'non'}  zone={'OUI' if in_zone else 'non'}  dist={distance:.2f}m",
            _COLOR_OK if (player_present and in_zone) else _COLOR_WARN,
        ),
        (
            "calibre" if calibrated else "NON calibre - bras tendus 3s",
            _COLOR_OK if calibrated else _COLOR_WARN,
        ),
    ]
    y = 24
    for text, color in lines:
        _text(canvas, text, (12, y), color, 0.55)
        y += 24

    bars = (
        ("lean", gesture_out.lean, True),
        ("lift", gesture_out.lift, False),
        ("throttle", gesture_out.throttle, True),
        ("glide", gesture_out.glide, False),
    )
    # Barres ancrées en bas : au centre-gauche elles recouvraient le joueur, précisément la
    # zone qu'on regarde pour juger l'overlay.
    y = canvas.shape[0] - 34 - 22 * len(bars)
    for label, value, signed in bars:
        _draw_bar(canvas, 12, y, label, value, signed)
        y += 22

    _text(canvas, "q/Echap: quitter   f: brut <-> filtre", (12, canvas.shape[0] - 12), _COLOR_TEXT, 0.45)


def _draw_bar(canvas: np.ndarray, x: int, y: int, label: str, value: float, signed: bool) -> None:
    """Barre horizontale d'une commande. `signed` : valeur dans [-1,1], centrée sur le milieu."""
    bar_x = x + 78
    bar_w, bar_h = 150, 12
    cv2.rectangle(canvas, (bar_x, y), (bar_x + bar_w, y + bar_h), _COLOR_BAR_BG, -1)

    value = max(-1.0, min(1.0, float(value)))
    if signed:
        mid = bar_x + bar_w // 2
        end = mid + int(value * bar_w / 2)
        cv2.rectangle(canvas, (min(mid, end), y), (max(mid, end), y + bar_h), _COLOR_LOCKED, -1)
        cv2.line(canvas, (mid, y), (mid, y + bar_h), _COLOR_TEXT, 1)
    else:
        cv2.rectangle(canvas, (bar_x, y), (bar_x + int(max(0.0, value) * bar_w), y + bar_h), _COLOR_LOCKED, -1)

    _text(canvas, label, (x, y + bar_h - 1), _COLOR_TEXT, 0.45)
    _text(canvas, f"{value:+.2f}", (bar_x + bar_w + 8, y + bar_h - 1), _COLOR_TEXT, 0.45)


def _text(canvas: np.ndarray, text: str, org: tuple[int, int], color, scale: float) -> None:
    """Texte avec contour noir : reste lisible sur une image claire comme sur un fond noirci."""
    cv2.putText(canvas, text, org, cv2.FONT_HERSHEY_SIMPLEX, scale, (0, 0, 0), 3, cv2.LINE_AA)
    cv2.putText(canvas, text, org, cv2.FONT_HERSHEY_SIMPLEX, scale, color, 1, cv2.LINE_AA)
