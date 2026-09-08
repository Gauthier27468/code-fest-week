"""Test d'intégration : image réelle -> MediaPipe -> gestes -> paquet UDP.

Nécessite le modèle `models/pose_landmarker_lite.task` (téléchargé par run_bridge.sh) et
l'image de test `tests/fixtures/person.jpg`. Se skippe proprement si l'un des deux manque.

C'est ce test qui a révélé que `lean` saturait à ±1.0 pour une personne parfaitement droite :
les tests unitaires utilisaient des coordonnées synthétiques avec la convention gauche/droite
inversée par rapport à ce que MediaPipe produit réellement. Ne pas le supprimer.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

ROOT = Path(__file__).resolve().parent.parent
MODEL = ROOT / "models" / "pose_landmarker_lite.task"
IMAGE = ROOT / "tests" / "fixtures" / "person.jpg"


def _available() -> bool:
    return MODEL.exists() and IMAGE.exists()


def test_real_image_end_to_end():
    if not _available():
        print("  (skip: modèle ou image de test absent)")
        return

    import cv2

    from kibird_bridge import protocol
    from kibird_bridge.bridge import _build_packet, _hip_mid_pixel
    from kibird_bridge.gestures import GestureConfig, GestureState, update_gestures
    from kibird_bridge.pose import PoseEstimator
    from kibird_bridge.tracking import PlayerTracker

    rgb = cv2.cvtColor(cv2.imread(str(IMAGE)), cv2.COLOR_BGR2RGB)
    estimator = PoseEstimator(MODEL, num_poses=3)
    try:
        skeletons = estimator.detect(rgb)
        assert skeletons, "aucune pose détectée sur une image contenant une personne"
        sk = skeletons[0]

        # Toutes les articulations du contrat doivent être présentes
        assert len(sk) == protocol.JOINT_COUNT
        for name in protocol.JOINT_NAMES:
            assert name in sk

        # Cohérence anatomique : nez au-dessus des épaules, épaules au-dessus des hanches
        assert sk["NOSE"].y < sk["L_SHOULDER"].y
        assert sk["L_SHOULDER"].y < sk["L_HIP"].y
        assert sk["R_SHOULDER"].y < sk["R_HIP"].y
        # Convention MediaPipe : le côté anatomique gauche est à droite de l'image
        assert sk["L_SHOULDER"].x > sk["R_SHOULDER"].x, (
            "convention gauche/droite inattendue — le calcul de `lean` en dépend"
        )

        tracker = PlayerTracker()
        state = GestureState(neutral_distance_m=2.3)
        config = GestureConfig()
        now = 0.0
        out = None
        result = None
        for _ in range(40):  # ~1.3 s : laisse `glide` monter jusqu'à son palier
            now += 1 / 30.0
            _px, _py, hip_x, hip_y = _hip_mid_pixel(sk, rgb.shape[1], rgb.shape[0])
            result = tracker.update(2.3, hip_x, hip_y, now=now)
            out = update_gestures(
                state, config, now=now, dt=1 / 30.0,
                l_shoulder=(sk["L_SHOULDER"].x, sk["L_SHOULDER"].y),
                r_shoulder=(sk["R_SHOULDER"].x, sk["R_SHOULDER"].y),
                l_wrist=(sk["L_WRIST"].x, sk["L_WRIST"].y),
                r_wrist=(sk["R_WRIST"].x, sk["R_WRIST"].y),
                distance_m=2.3,
            )

        assert result.player_present and result.in_zone
        # Sujet debout et droit : aucune commande de direction parasite
        assert abs(out.lean) < 0.2, f"personne droite doit donner lean~0, obtenu {out.lean}"
        # Sujet bras tendus à l'horizontale : posture de plané reconnue. `glide` est une mesure
        # CONTINUE (voir gestures.py) : une vraie photo n'aligne jamais poignets et épaules
        # parfaitement, donc on n'attend pas 1.0 pile, seulement "clairement en train de planer".
        assert out.glide > 0.75, f"bras tendus doivent donner un glide élevé, obtenu {out.glide}"

        raw = protocol.pack(_build_packet(1, time.time(), result, 2.3, out, sk))
        assert len(raw) == protocol.PACKET_SIZE
        decoded = protocol.unpack(raw)
        assert decoded.player_present is True
        assert abs(decoded.glide - out.glide) < 1e-5
    finally:
        estimator.close()


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
