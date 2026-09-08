"""Orchestration principale : Kinect -> pose -> tracking -> gestes -> UDP (KiBird).

Voir implementation_plan.md pour l'architecture complète et le contrat réseau (section 2).
"""
from __future__ import annotations

import argparse
import socket
import sys
import time
from pathlib import Path

from . import protocol
from .gestures import GestureConfig, GestureState, update_gestures
from .recorder import SessionRecorder, replay_realtime
from .tracking import PlayerTracker, TrackingConfig

DEFAULT_MODEL_PATH = Path(__file__).resolve().parent.parent / "models" / "pose_landmarker_lite.task"
CALIBRATION_HOLD_S = 3.0  # maintien de la posture glide pour valider le démarrage (AGENTS.md)
TARGET_HZ = 30.0


def _hip_mid_pixel(skeleton: dict, width: int, height: int) -> tuple[int, int, float, float]:
    lx, ly = skeleton["L_HIP"].x, skeleton["L_HIP"].y
    rx, ry = skeleton["R_HIP"].x, skeleton["R_HIP"].y
    mx, my = (lx + rx) / 2.0, (ly + ry) / 2.0
    return int(mx * width), int(my * height), mx, my


def _shoulder_width_distance_fallback(skeleton: dict) -> float:
    """Estimation grossière par échelle apparente, pour développer SANS Kinect branchée.

    ⚠️ Non utilisée en conditions JPO (cf. implementation_plan.md 3.1) : dépend de la
    morphologie de la personne, donc non fiable pour un gate de zone déterministe.
    """
    lx, ly = skeleton["L_SHOULDER"].x, skeleton["L_SHOULDER"].y
    rx, ry = skeleton["R_SHOULDER"].x, skeleton["R_SHOULDER"].y
    width_norm = ((rx - lx) ** 2 + (ry - ly) ** 2) ** 0.5
    if width_norm < 1e-6:
        return 0.0
    # Calibré très approximativement : largeur d'épaules ~0.18 (normalisé) à 2m.
    return 0.18 * 2.0 / width_norm


def _build_packet(
    seq: int,
    timestamp: float,
    tracking_result,
    distance: float,
    gesture_out,
    skeleton: dict | None,
    replay_mode: bool = False,
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
        replay_mode=replay_mode,
        distance=distance,
        lean=gesture_out.lean,
        lift=gesture_out.lift,
        throttle=gesture_out.throttle,
        glide=gesture_out.glide,
        confidence=confidence,
        joints=tuple(joints),
    )


def _grab_first_frame(capture, timeout_s: float = 8.0):
    """Capture la première frame avec un chien de garde, avant d'annoncer qu'on émet.

    `freenect.sync_get_video()` peut se bloquer indéfiniment quand le device USB est dans un
    état bancal (constaté : process vivant, 0 paquet émis, aucun message). Sans ce garde-fou
    le bridge affiche "Émission UDP..." et ne fait plus rien — le pire mode de panne possible
    un jour de JPO, puisque tout a l'air normal côté console.
    """
    import threading

    from .capture import KinectUnavailableError

    result: dict = {}

    def worker():
        try:
            result["frame"] = capture.read()
        except BaseException as exc:  # noqa: BLE001 - remonté tel quel au thread principal
            result["error"] = exc

    # daemon : si sync_get_video() ne rend jamais la main, le thread reste bloqué mais le
    # process peut quand même sortir proprement pour laisser l'animateur relancer.
    t = threading.Thread(target=worker, daemon=True)
    t.start()
    t.join(timeout_s)

    if t.is_alive():
        raise KinectUnavailableError(
            f"La Kinect ne renvoie aucune image (bloquée depuis {timeout_s:.0f}s).\n"
            "  La caméra est détectée mais le flux ne démarre pas — typiquement un device USB\n"
            "  resté dans un état bancal après un arrêt brutal.\n"
            "  -> Débranche puis rebranche le câble USB de la Kinect, et relance."
        )
    if "error" in result:
        raise result["error"]
    return result["frame"]


def run_demo(args: argparse.Namespace) -> None:
    """Émet un joueur simulé à 30 Hz, sans Kinect, sans mediapipe et sans fichier de session.

    Sert à valider la liaison bridge -> Unity (port, parsing, mapping des commandes sur
    MoveBird) quand la Kinect n'est pas disponible : c'est la seule différence avec le mode
    live, les paquets émis sont strictement au même format.
    """
    import math

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"[DEMO] joueur simulé -> {args.host}:{args.port} @ {TARGET_HZ:.0f}Hz. Ctrl+C pour arrêter.")

    seq = 0
    t0 = time.time()
    frame_period = 1.0 / TARGET_HZ
    try:
        while True:
            loop_start = time.time()
            t = loop_start - t0

            # Cycle lisible à l'œil : virage lent d'un bord à l'autre, battements réguliers,
            # et une oscillation de vitesse plus lente pour voir bouger les trois axes.
            lean = math.sin(t * 0.5)
            lift = max(0.0, math.sin(t * 2.0))
            throttle = 0.6 * math.sin(t * 0.25)
            glide = 1.0 if abs(lift) < 0.05 else 0.0
            distance = 2.5 - throttle

            # Squelette factice cohérent avec les commandes, pour que l'overlay de debug
            # affiche autre chose qu'un bonhomme figé.
            roll = lean * 0.08
            wrist_y = 0.40 - lift * 0.25
            joints = [
                protocol.Joint(0.50, 0.20, distance, 0.95),                      # NOSE
                protocol.Joint(0.58, 0.35 + roll, distance, 0.95),               # L_SHOULDER
                protocol.Joint(0.42, 0.35 - roll, distance, 0.95),               # R_SHOULDER
                protocol.Joint(0.68, 0.38 + roll, distance, 0.90),               # L_ELBOW
                protocol.Joint(0.32, 0.38 - roll, distance, 0.90),               # R_ELBOW
                protocol.Joint(0.78, wrist_y + roll, distance, 0.85),            # L_WRIST
                protocol.Joint(0.22, wrist_y - roll, distance, 0.85),            # R_WRIST
                protocol.Joint(0.56, 0.65, distance, 0.90),                      # L_HIP
                protocol.Joint(0.44, 0.65, distance, 0.90),                      # R_HIP
            ]

            packet = protocol.SkeletonPacket(
                seq=seq,
                timestamp=loop_start,
                player_present=True,
                in_zone=True,
                replay_mode=True,
                distance=distance,
                lean=lean,
                lift=lift,
                throttle=throttle,
                glide=glide,
                confidence=0.9,
                joints=tuple(joints),
            )
            sock.sendto(protocol.pack(packet), (args.host, args.port))
            seq += 1

            remaining = frame_period - (time.time() - loop_start)
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        print("\nArrêt.")
    finally:
        sock.close()


def run_replay(args: argparse.Namespace) -> None:
    """Rejoue une session enregistrée : ne touche ni à la Kinect ni à mediapipe.

    C'est le mode à utiliser pour tester l'intégration Unity sans matériel
    (implementation_plan.md 3.4 et Plan de vérification P4).
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"[REPLAY] {args.replay} -> {args.host}:{args.port} (boucle={not args.no_loop})")
    try:
        for raw in replay_realtime(args.replay, loop=not args.no_loop):
            sock.sendto(raw, (args.host, args.port))
    except KeyboardInterrupt:
        print("\nArrêt.")
    finally:
        sock.close()


def run_live(args: argparse.Namespace) -> None:
    # Imports différés : la Kinect et mediapipe ne sont nécessaires qu'en mode live,
    # jamais en mode --replay (cf. run_replay ci-dessus).
    from .capture import KinectCapture
    from .pose import PoseEstimator

    print(f"Chargement du modèle {args.model} ...")
    pose_estimator = PoseEstimator(args.model, num_poses=args.num_poses)
    capture = KinectCapture(use_registered_depth=not args.no_depth)
    print("Kinect initialisée.")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    recorder = SessionRecorder(args.record) if args.record else None

    tracker = PlayerTracker(TrackingConfig(min_distance_m=args.min_distance, max_distance_m=args.max_distance))
    gesture_state = GestureState()
    gesture_config = GestureConfig()

    seq = 0
    glide_hold_start: float | None = None
    frame_period = 1.0 / TARGET_HZ
    last_t = time.time()

    # Le premier appel valide réellement le flux : tant qu'il n'a pas rendu la main, on n'a
    # aucune preuve que la Kinect produit des images.
    print("Attente de la première image ...", flush=True)
    first_frame = _grab_first_frame(capture)
    print("Flux vidéo OK.")

    print(f"Émission UDP vers {args.host}:{args.port} @ ~{TARGET_HZ:.0f}Hz. Ctrl+C pour arrêter.")
    frames_since_status = 0
    last_status_t = time.time()
    try:
        while True:
            loop_start = time.time()
            frame = first_frame if first_frame is not None else capture.read()
            first_frame = None
            now = time.time()
            dt = max(now - last_t, 1e-3)
            last_t = now

            skeletons = pose_estimator.detect(frame.rgb)
            candidate = skeletons[0] if skeletons else None

            distance = 0.0
            hip_x = hip_y = None
            if candidate is not None:
                px, py, hip_x, hip_y = _hip_mid_pixel(candidate, frame.rgb.shape[1], frame.rgb.shape[0])
                if args.no_depth:
                    distance = _shoulder_width_distance_fallback(candidate)
                else:
                    distance = capture.median_depth_at(frame.depth_mm, px, py)

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
                # Calibration : maintien de la posture glide 3s pour figer la distance neutre
                # (implementation_plan.md : "maintenir 3s pour valider le démarrage").
                if gesture_state.neutral_distance_m is None:
                    if gesture_out.glide > 0.95:
                        if glide_hold_start is None:
                            glide_hold_start = now
                        elif now - glide_hold_start >= CALIBRATION_HOLD_S:
                            gesture_state.neutral_distance_m = distance
                            print(f"\n[CALIBRATION] Distance neutre fixée à {distance:.2f}m")
                    else:
                        glide_hold_start = None
                if args.mirror:
                    gesture_out.lean = -gesture_out.lean
            else:
                from .gestures import GestureOutput
                gesture_out = GestureOutput(lean=0.0, lift=0.0, throttle=0.0, glide=0.0)
                glide_hold_start = None

            packet = _build_packet(seq, frame.timestamp, tracking_result, distance, gesture_out, candidate)
            raw = protocol.pack(packet)
            sock.sendto(raw, (args.host, args.port))
            if recorder is not None:
                recorder.write(raw, now=now)
            seq += 1

            # Ligne de statut régulière : sans elle, un blocage de la capture est indiscernable
            # d'un fonctionnement normal (la console reste muette dans les deux cas).
            frames_since_status += 1
            if now - last_status_t >= 2.0:
                hz = frames_since_status / (now - last_status_t)
                if tracking_result.player_present:
                    calib = "calibré" if gesture_state.neutral_distance_m is not None else "NON calibré (bras tendus 3s)"
                    etat = f"JOUEUR  dist={distance:.2f}m  {calib}"
                else:
                    etat = "aucun joueur dans la zone"
                print(f"\r[{hz:4.1f} Hz] {etat}".ljust(78), end="", flush=True)
                frames_since_status = 0
                last_status_t = now

            elapsed = time.time() - loop_start
            remaining = frame_period - elapsed
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        print("\nArrêt.")
    finally:
        sock.close()
        capture.close()
        pose_estimator.close()
        if recorder is not None:
            recorder.close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--model", default=str(DEFAULT_MODEL_PATH), help="Chemin du modèle .task PoseLandmarker")
    parser.add_argument("--num-poses", type=int, default=3, help="Nombre max de personnes détectées par MediaPipe")
    parser.add_argument("--mirror", action=argparse.BooleanOptionalAction, default=False,
                        help="Inverse gauche/droite. Le mapping par défaut est déjà naturel "
                             "(le joueur penche vers sa gauche -> l'oiseau va à gauche) : "
                             "n'active ce flag que si le ressenti est inversé à l'installation.")
    parser.add_argument("--no-depth", action="store_true",
                        help="Fallback dev SANS Kinect : distance estimée par échelle d'épaules (non fiable, cf. plan)")
    parser.add_argument("--min-distance", type=float, default=1.0)
    parser.add_argument("--max-distance", type=float, default=4.0)
    parser.add_argument("--record", metavar="FICHIER.kbr", help="Enregistre la session en parallèle de l'émission live")
    parser.add_argument("--replay", metavar="FICHIER.kbr", help="Rejoue une session enregistrée au lieu d'utiliser la Kinect")
    parser.add_argument("--no-loop", action="store_true", help="Avec --replay : ne pas boucler à la fin du fichier")
    parser.add_argument("--demo", action="store_true",
                        help="Joueur simulé, sans Kinect ni fichier : pour vérifier la liaison avec Unity")
    args = parser.parse_args()

    if args.demo:
        run_demo(args)
        return

    if args.replay:
        run_replay(args)
        return

    from .capture import KinectUnavailableError

    try:
        run_live(args)
    except KinectUnavailableError as exc:
        # Message déjà rédigé pour être lisible par un animateur, pas par un développeur :
        # pas de traceback, et un code de sortie distinct pour un éventuel script de supervision.
        print(f"\n{exc}\n", file=sys.stderr)
        raise SystemExit(2)


if __name__ == "__main__":
    main()
