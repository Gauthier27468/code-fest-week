"""Récepteur console : valide le bridge sans Unity (implementation_plan.md, Plan de vérification P1).

⚠️ Ne pas utiliser `nc -lu 7777` pour ce test : nc bind le port UDP en exclusivité, ce qui
empêche ensuite Unity de l'ouvrir (ou pire, lui vole les paquets). Utiliser exclusivement ce
script, qui `SO_REUSEADDR` proprement et se contente d'observer.

Usage : python -m kibird_bridge.monitor [--port 7777]
"""
from __future__ import annotations

import argparse
import socket
import time

from . import protocol


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7777)
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.host, args.port))
    print(f"En écoute sur {args.host}:{args.port} — Ctrl+C pour arrêter.\n")

    last_seq: int | None = None
    last_recv_time = time.time()
    packet_count = 0
    t_start = time.time()

    try:
        while True:
            sock.settimeout(1.0)
            try:
                data, _addr = sock.recvfrom(4096)
            except socket.timeout:
                gap = time.time() - last_recv_time
                if gap > 1.0:
                    print(f"\r[ALERTE] Aucun paquet depuis {gap:.1f}s — bridge probablement mort".ljust(100), end="")
                continue

            now = time.time()
            last_recv_time = now
            packet_count += 1

            try:
                pkt = protocol.unpack(data)
            except ValueError as e:
                print(f"\n[REJETÉ] Paquet invalide : {e}")
                continue

            if last_seq is not None and pkt.seq <= last_seq:
                print(f"\n[HORS ORDRE] seq={pkt.seq} <= dernier reçu {last_seq}, ignoré côté Unity")
            last_seq = pkt.seq

            latency_ms = (now - pkt.timestamp) * 1000.0
            elapsed = now - t_start
            rate = packet_count / elapsed if elapsed > 0 else 0.0

            status = "JOUEUR" if pkt.player_present else "attente"
            zone = "in" if pkt.in_zone else "out"
            replay_tag = " [REPLAY]" if pkt.replay_mode else ""

            line = (
                f"\rseq={pkt.seq:>6} | {status:>7} (zone={zone}) | dist={pkt.distance:.2f}m | "
                f"lean={pkt.lean:+.2f} lift={pkt.lift:+.2f} thr={pkt.throttle:+.2f} glide={pkt.glide:.2f} | "
                f"latence={latency_ms:6.1f}ms | {rate:5.1f} Hz{replay_tag}"
            )
            print(line.ljust(120), end="")
    except KeyboardInterrupt:
        print("\nArrêt.")
    finally:
        sock.close()


if __name__ == "__main__":
    main()
