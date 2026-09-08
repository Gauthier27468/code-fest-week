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

    print(f"Émission UDP vers {args.host}:{args.port} @ ~{TARGET_HZ:.0f}Hz. Ctrl+C pour arrêter.")
    try:
        while True:
            loop_start = time.time()
            frame = capture.read()
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
    args = parser.parse_args()

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
