"""Orchestration principale : Kinect -> pose -> tracking -> gestes -> UDP."""
from __future__ import annotations

import argparse
import socket
import sys
import time
from pathlib import Path

from .autocenter import focus as autocenter_focus, reset as autocenter_reset
from . import protocol
from .gestures import GestureConfig, GestureOutput, GestureState, update_gestures
from .tracking import PlayerTracker, TrackingConfig


def _default_model_path() -> Path:
    """PyInstaller extrait le modele dans sys._MEIPASS ; hors bundle il vit dans models/."""
    bundle_dir = getattr(sys, "_MEIPASS", None)
    if bundle_dir is not None:
        return Path(bundle_dir) / "models" / "pose_landmarker_lite.task"
    return Path(__file__).resolve().parent.parent / "models" / "pose_landmarker_lite.task"


DEFAULT_MODEL_PATH = _default_model_path()

# Maintien de la posture bras tendus validant le demarrage.
CALIBRATION_HOLD_S = 3.0
# En dessous, la tete est occluse ou devinee : piloter le moteur dessus le fait divaguer.
AUTOCENTER_MIN_VISIBILITY = 0.5
TARGET_HZ = 30.0

def _hip_mid_pixel(skeleton: dict, width: int, height: int) -> tuple[int, int, float, float]:
    lx, ly = skeleton["L_HIP"].x, skeleton["L_HIP"].y
    rx, ry = skeleton["R_HIP"].x, skeleton["R_HIP"].y
    mx, my = (lx + rx) / 2.0, (ly + ry) / 2.0
    return int(mx * width), int(my * height), mx, my


def _select_front_skeleton(
    skeletons: list[dict], capture, frame,
) -> tuple[dict | None, float, float | None, float | None]:
    """Retient le squelette dont la distance mesuree/estimee est la plus faible.

    La Kinect utilise sa profondeur IR. La webcam estime la distance avec la largeur apparente
    des epaules ; c'est moins precis, mais conserve la zone et la commande avancer/reculer.
    Retourne (squelette, distance_m, hip_x, hip_y) ; distance_m vaut 0.0 si aucune profondeur
    valide, auquel cas on retombe sur la detection la plus proeminente.
    """
    width, height = frame.rgb.shape[1], frame.rgb.shape[0]
    best_skeleton = None
    best_distance = None
    best_hip = (None, None)
    for skeleton in skeletons:
        px, py, hip_x, hip_y = _hip_mid_pixel(skeleton, width, height)
        distance = capture.distance_for_skeleton(skeleton, frame)
        if distance <= 0.0:
            continue
        if best_distance is None or distance < best_distance:
            best_skeleton, best_distance, best_hip = skeleton, distance, (hip_x, hip_y)

    if best_skeleton is not None:
        return best_skeleton, best_distance, best_hip[0], best_hip[1]

    if not skeletons:
        return None, 0.0, None, None

    skeleton = skeletons[0]
    _, _, hip_x, hip_y = _hip_mid_pixel(skeleton, width, height)
    return skeleton, 0.0, hip_x, hip_y


def _build_packet(
    seq: int,
    timestamp: float,
    tracking_result,
    distance: float,
    gesture_out,
    skeleton: dict | None,
) -> protocol.SkeletonPacket:
    joints = []
    for name in protocol.JOINT_NAMES:
        if skeleton is not None and name in skeleton:
            lm = skeleton[name]
            joints.append(protocol.Joint(x=lm.x, y=lm.y, z=lm.z, confidence=lm.visibility))
        else:
            joints.append(protocol.Joint())

    confidence = 0.0
    if skeleton is not None:
        confidence = sum(j.confidence for j in joints) / len(joints)

    return protocol.SkeletonPacket(
        seq=seq,
        timestamp=timestamp,
        player_present=tracking_result.player_present,
        in_zone=tracking_result.in_zone,
        distance=distance,
        lean=gesture_out.lean,
        lift=gesture_out.lift,
        throttle=gesture_out.throttle,
        glide=gesture_out.glide,
        confidence=confidence,
        joints=tuple(joints),
    )


def _grab_first_frame(capture, timeout_s: float):
    """Capture la premiere frame avec un chien de garde.

    sync_get_video() peut se bloquer indefiniment quand le device USB est dans un etat bancal :
    sans ce garde-fou le bridge annonce "Emission UDP..." et ne fait plus rien.
    """
    import threading

    from .capture import CaptureUnavailableError

    result: dict = {}

    def worker():
        try:
            result["frame"] = capture.read()
        except BaseException as exc:  # noqa: BLE001
            result["error"] = exc

    t = threading.Thread(target=worker, daemon=True)
    t.start()
    t.join(timeout_s)

    if t.is_alive():
        raise CaptureUnavailableError(
            f"La source {capture.source_name} ne renvoie aucune image "
            f"(bloquee depuis {timeout_s:.0f}s)."
        )
    if "error" in result:
        raise result["error"]
    return result["frame"]


def _create_capture(kind: str, args: argparse.Namespace):
    from .capture import KinectCapture, WebcamCapture

    if kind == "kinect":
        return KinectCapture()
    return WebcamCapture(
        device=args.webcam_device,
        width=args.webcam_width,
        height=args.webcam_height,
        fps=args.webcam_fps,
        horizontal_fov_deg=args.webcam_horizontal_fov_deg,
        shoulder_width_m=args.estimated_shoulder_width_m,
    )


def _open_capture(kind: str, args: argparse.Namespace):
    """Construit une source et valide son premier frame, avec retries pour la Kinect."""
    from .capture import CaptureUnavailableError

    attempts = args.kinect_init_attempts if kind == "kinect" else 1
    last_error: CaptureUnavailableError | None = None
    capture = None
    for attempt in range(1, attempts + 1):
        capture = _create_capture(kind, args)
        try:
            first_frame = _grab_first_frame(capture, args.kinect_init_timeout_seconds)
            return capture, first_frame
        except CaptureUnavailableError as exc:
            last_error = exc
            capture.close()
            if attempt < attempts:
                print(
                    f"\n[{kind}] tentative {attempt}/{attempts} sans image, nouvel essai "
                    f"dans {args.kinect_retry_delay_seconds:.1f}s...",
                    file=sys.stderr,
                )
                time.sleep(args.kinect_retry_delay_seconds)
    raise last_error or CaptureUnavailableError(f"Source {kind} indisponible.")


def run_live(args: argparse.Namespace) -> None:
    from .capture import CaptureUnavailableError, kinect_is_connected
    from .pose import PoseEstimator
    from .segmentation import remove_background

    preview = None
    if args.preview:
        # Import differe comme le reste : --preview a besoin d'un OpenCV avec HighGUI, dont
        # le mode nominal (sans preview) ne doit pas dependre.
        from .preview import PreviewClosed, PreviewWindow

        preview = PreviewWindow(scale=args.preview_scale)

    print(f"Chargement du modele {args.model} ...")
    pose_estimator = PoseEstimator(args.model, num_poses=args.num_poses)
    preferred = args.capture_mode
    if preferred == "auto":
        preferred = "kinect" if kinect_is_connected() else "webcam"
        if preferred == "webcam":
            print("Aucune Kinect detectee sur le bus USB : fallback webcam.")

    print(f"Initialisation de la source {preferred} ...")
    try:
        capture, first_frame = _open_capture(preferred, args)
    except CaptureUnavailableError as exc:
        if args.capture_mode != "auto" or preferred == "webcam":
            pose_estimator.close()
            raise
        print(f"\nKinect inutilisable ({exc})\nFallback webcam ...", file=sys.stderr)
        capture, first_frame = _open_capture("webcam", args)

    print(f"Source active : {capture.source_name}.")
    # Le filtre de fond necessite la profondeur registered de la Kinect.
    bg_filter_enabled = args.bg_filter and capture.has_depth
    if args.bg_filter and not capture.has_depth:
        print("Filtre de fond IR ignore en mode webcam (aucune profondeur disponible).")
    if bg_filter_enabled:
        print(f"Filtre de fond IR actif : tout ce qui est au-dela de {args.bg_max_distance:.1f}m est masque.")
        if args.num_poses > 1:
            print(f"  (--num-poses {args.num_poses} : MediaPipe cherche plusieurs poses, ~25 ms/frame "
                  f"de plus. 1 suffit tant que le filtre isole bien le joueur.)")
    else:
        print("Filtre de fond IR desactive.")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    tracker = PlayerTracker(TrackingConfig(min_distance_m=args.min_distance, max_distance_m=args.max_distance))
    gesture_state = GestureState()
    gesture_config = GestureConfig()

    seq = 0
    glide_hold_start: float | None = None
    frame_period = 1.0 / TARGET_HZ
    last_t = time.time()

    print("Flux video OK.")

    if preview is not None:
        print("[PREVIEW] Fenetre de debug active (q/Echap pour quitter, f pour basculer brut/filtre).")
    print(f"Emission UDP vers {args.host}:{args.port} @ ~{TARGET_HZ:.0f}Hz. Ctrl+C pour arreter.")
    frames_since_status = 0
    hz_ema = 0.0
    last_status_t = time.time()
    try:
        while True:
            loop_start = time.time()
            frame = first_frame if first_frame is not None else capture.read()
            first_frame = None
            now = time.time()
            dt = max(now - last_t, 1e-3)
            last_t = now

            # Fond supprime AVANT MediaPipe : les personnes au-dela de la zone de jeu ne
            # generent plus de squelette du tout, au lieu d'etre filtrees apres coup.
            rgb_for_pose = (
                remove_background(frame.rgb, frame.depth_mm, max_depth_m=args.bg_max_distance)
                if bg_filter_enabled
                else frame.rgb
            )
            skeletons = pose_estimator.detect(rgb_for_pose)
            candidate, distance, hip_x, hip_y = _select_front_skeleton(
                skeletons, capture, frame,
            )

            tracking_result = tracker.update(distance, hip_x, hip_y, now=now)

            if tracking_result.player_present and candidate is not None:
                gesture_out = update_gestures(
                    gesture_state, gesture_config, now=now, dt=dt,
                    l_shoulder=(candidate["L_SHOULDER"].x, candidate["L_SHOULDER"].y),
                    r_shoulder=(candidate["R_SHOULDER"].x, candidate["R_SHOULDER"].y),
                    l_wrist=(candidate["L_WRIST"].x, candidate["L_WRIST"].y),
                    r_wrist=(candidate["R_WRIST"].x, candidate["R_WRIST"].y),
                    distance_m=distance,
                )
                # Posture bras tendus tenue CALIBRATION_HOLD_S : fige la distance neutre.
                if gesture_state.neutral_distance_m is None:
                    if gesture_out.glide > 0.95:
                        if glide_hold_start is None:
                            glide_hold_start = now
                        elif now - glide_hold_start >= CALIBRATION_HOLD_S:
                            gesture_state.neutral_distance_m = distance
                            print(f"\n[CALIBRATION] Distance neutre fixee a {distance:.2f}m")
                    else:
                        glide_hold_start = None
                if args.mirror:
                    gesture_out.lean = -gesture_out.lean
            else:
                gesture_out = GestureOutput(lean=0.0, lift=0.0, throttle=0.0, glide=0.0)
                glide_hold_start = None

            if args.auto_center and capture.has_motor:
                head = candidate.get("NOSE") if candidate is not None else None
                if head is not None and head.visibility >= AUTOCENTER_MIN_VISIBILITY:
                    autocenter_focus((head.x, head.y))
                elif not tracking_result.player_present:
                    autocenter_reset()

            packet = _build_packet(seq, frame.timestamp, tracking_result, distance, gesture_out, candidate)
            sock.sendto(protocol.pack(packet), (args.host, args.port))
            seq += 1

            # Cadence instantanee lissee : la ligne de statut ne la calcule que toutes les
            # 2s, trop lent pour une fenetre rafraichie a chaque frame.
            hz_ema = 1.0 / dt if hz_ema == 0.0 else 0.9 * hz_ema + 0.1 / dt

            if preview is not None:
                try:
                    preview.show(
                        frame.rgb, rgb_for_pose, skeletons, candidate,
                        distance=distance,
                        in_zone=tracking_result.in_zone,
                        player_present=tracking_result.player_present,
                        calibrated=gesture_state.neutral_distance_m is not None,
                        gesture_out=gesture_out,
                        hz=hz_ema,
                    )
                except PreviewClosed:
                    print("\n[PREVIEW] Fenetre fermee, arret du bridge.")
                    break

            # Sans cette ligne de statut, un blocage de la capture est indiscernable d'un
            # fonctionnement normal : la console reste muette dans les deux cas.
            frames_since_status += 1
            if now - last_status_t >= 2.0:
                hz = frames_since_status / (now - last_status_t)
                if tracking_result.player_present:
                    calib = "calibre" if gesture_state.neutral_distance_m is not None else "NON calibre (bras tendus 3s)"
                    etat = f"JOUEUR  dist={distance:.2f}m  {calib}"
                else:
                    etat = "aucun joueur dans la zone"
                print(f"\r[{hz:4.1f} Hz] {etat}".ljust(78), end="", flush=True)
                frames_since_status = 0
                last_status_t = now

            remaining = frame_period - (time.time() - loop_start)
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        print("\nArret.")
    finally:
        sock.close()
        capture.close()
        pose_estimator.close()
        if preview is not None:
            preview.close()


def main() -> None:
    from .config import ConfigError, load_or_create

    # En execution depuis les sources : bridge.py -> kibird_bridge -> kinect_bridge -> tools
    # -> racine Unity. Le launcher passe de toute facon un chemin absolu dans le build gele.
    default_config_path = Path(__file__).resolve().parents[3] / "kibird-config.toml"
    config_parser = argparse.ArgumentParser(add_help=False)
    config_parser.add_argument("--config", default=str(default_config_path))
    config_args, _ = config_parser.parse_known_args()
    try:
        config, created = load_or_create(config_args.config)
    except ConfigError as exc:
        print(f"\nConfiguration invalide : {exc}\n", file=sys.stderr)
        raise SystemExit(2)
    config_path = Path(config_args.config).expanduser().resolve()
    print(f"Configuration {'creee' if created else 'chargee'} : {config_path}")

    parser = argparse.ArgumentParser(description=__doc__, parents=[config_parser])
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--model", default=str(DEFAULT_MODEL_PATH), help="Chemin du modele .task PoseLandmarker")
    parser.add_argument("--capture-mode", choices=("auto", "kinect", "webcam"), default=config.capture_mode,
                        help="Source video. Surcharge [capture].mode du fichier de configuration")
    parser.add_argument("--webcam-device", type=int, default=config.webcam_device)
    parser.add_argument("--webcam-width", type=int, default=config.webcam_width)
    parser.add_argument("--webcam-height", type=int, default=config.webcam_height)
    parser.add_argument("--webcam-fps", type=float, default=config.webcam_fps)
    parser.add_argument("--webcam-horizontal-fov-deg", type=float, default=config.webcam_horizontal_fov_deg)
    parser.add_argument("--estimated-shoulder-width-m", type=float, default=config.estimated_shoulder_width_m)
    parser.add_argument("--kinect-init-attempts", type=int, default=config.kinect_init_attempts)
    parser.add_argument("--kinect-init-timeout-seconds", type=float, default=config.kinect_init_timeout_seconds)
    parser.add_argument("--kinect-retry-delay-seconds", type=float, default=config.kinect_retry_delay_seconds)
    parser.add_argument("--num-poses", type=int, default=config.num_poses,
                        help="Nombre max de personnes detectees par MediaPipe. 1 par defaut : le "
                             "filtre de fond ne laisse deja qu'une personne dans l'image, et "
                             "au-dela de 1 MediaPipe perd sa fast-path de suivi (31 ms -> 56 ms "
                             "par frame). Passer a 2 si deux visiteurs sont confondus.")
    parser.add_argument("--mirror", action=argparse.BooleanOptionalAction, default=config.mirror,
                        help="Inverse gauche/droite. Le mapping par defaut est deja naturel : "
                             "n'active ce flag que si le ressenti est inverse a l'installation.")
    parser.add_argument("--bg-filter", action=argparse.BooleanOptionalAction, default=config.background_filter,
                        help="Masque le fond au-dela de --bg-max-distance avec la profondeur IR "
                             "avant d'envoyer l'image a MediaPipe")
    parser.add_argument("--bg-max-distance", type=float, default=config.background_max_distance_m,
                        help="Distance (m) au-dela de laquelle les pixels sont noircis")
    parser.add_argument("--preview", action=argparse.BooleanOptionalAction, default=config.preview,
                        help="Ouvre une fenetre camera avec overlay du squelette MediaPipe et des "
                             "commandes deduites (debug/reglage ; necessite opencv-python non-headless)")
    parser.add_argument("--preview-scale", type=float, default=config.preview_scale,
                        help="Facteur d'echelle de la fenetre --preview (ex. 0.5 pour une demi-taille)")
    parser.add_argument("--min-distance", type=float, default=config.min_distance_m)
    parser.add_argument("--max-distance", type=float, default=config.max_distance_m)
    parser.add_argument("--auto-center", action=argparse.BooleanOptionalAction, default=config.auto_center)
    args = parser.parse_args()

    from .capture import CaptureUnavailableError

    try:
        run_live(args)
    except CaptureUnavailableError as exc:
        # Message destine a un animateur, pas a un developpeur : pas de traceback,
        # et un code de sortie distinct pour un script de supervision.
        print(f"\n{exc}\n", file=sys.stderr)
        raise SystemExit(2)


if __name__ == "__main__":
    main()
