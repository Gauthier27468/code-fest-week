import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge import protocol
from kibird_bridge.recorder import SessionRecorder, replay


def test_record_and_replay_roundtrip():
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "session.kbr"
        packets = [
            protocol.pack(protocol.SkeletonPacket(seq=i, distance=2.0 + i * 0.01))
            for i in range(5)
        ]
        with SessionRecorder(path) as rec:
            for i, p in enumerate(packets):
                rec.write(p, now=100.0 + i * (1 / 30.0))

        replayed = list(replay(path))
        assert len(replayed) == 5
        for i, (rel_t, data) in enumerate(replayed):
            assert data == packets[i]
            assert abs(rel_t - i * (1 / 30.0)) < 1e-6

        decoded = protocol.unpack(replayed[3][1])
        assert decoded.seq == 3


def test_rejects_bad_file():
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "bad.kbr"
        path.write_bytes(b"NOPE0000")
        try:
            list(replay(path))
            assert False, "devrait lever ValueError"
        except ValueError as e:
            assert "magic" in str(e).lower()


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print(f"OK: {t.__name__}")
    print(f"\n{len(tests)} tests passés.")
