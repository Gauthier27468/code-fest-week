import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from kibird_bridge.config import ConfigError, load_or_create


def test_missing_config_is_created():
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "kibird-config.toml"
        config, created = load_or_create(path)
        assert created is True
        assert path.is_file()
        assert config.capture_mode == "auto"


def test_existing_config_selects_webcam():
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "kibird-config.toml"
        path.write_text('[capture]\nmode = "webcam"\nwebcam_device = 2\n', encoding="utf-8")
        config, created = load_or_create(path)
        assert created is False
        assert config.capture_mode == "webcam"
        assert config.webcam_device == 2


def test_invalid_mode_is_rejected():
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "kibird-config.toml"
        path.write_text('[capture]\nmode = "magique"\n', encoding="utf-8")
        try:
            load_or_create(path)
        except ConfigError:
            return
        raise AssertionError("un mode inconnu doit etre refuse")


if __name__ == "__main__":
    tests = [v for k, v in list(globals().items()) if k.startswith("test_")]
    for test in tests:
        test()
        print(f"OK: {test.__name__}")
    print(f"\n{len(tests)} tests passes.")
