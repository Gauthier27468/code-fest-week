"""Configuration persistante et lisible par un animateur pour le bridge KiBird."""
from __future__ import annotations

import tomllib
from dataclasses import dataclass
from pathlib import Path


DEFAULT_CONFIG_TEXT = """# Configuration KiBird - modifiable avec un editeur de texte.
# mode = "kinect" : Kinect obligatoire, aucun fallback silencieux.
# mode = "webcam" : webcam uniquement.
# mode = "auto"    : Kinect prioritaire, webcam si elle est absente ou inutilisable.

[capture]
mode = "auto"
webcam_device = 0
webcam_width = 640
webcam_height = 480
webcam_fps = 30
# Sert a estimer la distance sans capteur de profondeur. Ajuster si necessaire sur place.
webcam_horizontal_fov_deg = 70.0
estimated_shoulder_width_m = 0.38
kinect_init_attempts = 2
kinect_init_timeout_seconds = 4.0
kinect_retry_delay_seconds = 1.0

[tracking]
min_distance_m = 1.0
max_distance_m = 4.0
# Mettre 2 en mode webcam si des visiteurs passent souvent dans le cadre.
num_poses = 1

[image]
mirror = false
background_filter = true
background_max_distance_m = 4.0
auto_center = true

[debug]
preview = false
preview_scale = 1.0
"""


class ConfigError(ValueError):
    """Le fichier existe, mais une de ses valeurs n'est pas exploitable."""


@dataclass(frozen=True)
class BridgeConfig:
    capture_mode: str = "auto"
    webcam_device: int = 0
    webcam_width: int = 640
    webcam_height: int = 480
    webcam_fps: float = 30.0
    webcam_horizontal_fov_deg: float = 70.0
    estimated_shoulder_width_m: float = 0.38
    kinect_init_attempts: int = 2
    kinect_init_timeout_seconds: float = 4.0
    kinect_retry_delay_seconds: float = 1.0
    min_distance_m: float = 1.0
    max_distance_m: float = 4.0
    num_poses: int = 1
    mirror: bool = False
    background_filter: bool = True
    background_max_distance_m: float = 4.0
    auto_center: bool = True
    preview: bool = False
    preview_scale: float = 1.0


def _value(data: dict, section: str, key: str, expected_type, default):
    value = data.get(section, {}).get(key, default)
    # bool est une sous-classe de int en Python : ne pas accepter true comme index camera.
    if expected_type is int and (not isinstance(value, int) or isinstance(value, bool)):
        raise ConfigError(f"[{section}] {key} doit etre un entier")
    if expected_type is float and (not isinstance(value, (int, float)) or isinstance(value, bool)):
        raise ConfigError(f"[{section}] {key} doit etre un nombre")
    if expected_type is bool and not isinstance(value, bool):
        raise ConfigError(f"[{section}] {key} doit valoir true ou false")
    if expected_type is str and not isinstance(value, str):
        raise ConfigError(f"[{section}] {key} doit etre du texte")
    return value


def load_or_create(path: str | Path) -> tuple[BridgeConfig, bool]:
    """Charge ``path`` et cree un modele documente lorsqu'il est absent."""
    config_path = Path(path).expanduser().resolve()
    created = False
    if not config_path.exists():
        try:
            config_path.parent.mkdir(parents=True, exist_ok=True)
            config_path.write_text(DEFAULT_CONFIG_TEXT, encoding="utf-8")
        except OSError as exc:
            raise ConfigError(f"Impossible de creer {config_path}: {exc}") from exc
        created = True

    try:
        data = tomllib.loads(config_path.read_text(encoding="utf-8"))
    except (OSError, tomllib.TOMLDecodeError) as exc:
        raise ConfigError(f"Impossible de lire {config_path}: {exc}") from exc

    for section in ("capture", "tracking", "image", "debug"):
        if section in data and not isinstance(data[section], dict):
            raise ConfigError(f"[{section}] doit etre une section TOML")

    defaults = BridgeConfig()
    mode = _value(data, "capture", "mode", str, defaults.capture_mode).lower()
    if mode not in {"auto", "kinect", "webcam"}:
        raise ConfigError('[capture] mode doit valoir "auto", "kinect" ou "webcam"')

    config = BridgeConfig(
        capture_mode=mode,
        webcam_device=_value(data, "capture", "webcam_device", int, defaults.webcam_device),
        webcam_width=_value(data, "capture", "webcam_width", int, defaults.webcam_width),
        webcam_height=_value(data, "capture", "webcam_height", int, defaults.webcam_height),
        webcam_fps=float(_value(data, "capture", "webcam_fps", float, defaults.webcam_fps)),
        webcam_horizontal_fov_deg=float(_value(data, "capture", "webcam_horizontal_fov_deg", float, defaults.webcam_horizontal_fov_deg)),
        estimated_shoulder_width_m=float(_value(data, "capture", "estimated_shoulder_width_m", float, defaults.estimated_shoulder_width_m)),
        kinect_init_attempts=_value(data, "capture", "kinect_init_attempts", int, defaults.kinect_init_attempts),
        kinect_init_timeout_seconds=float(_value(data, "capture", "kinect_init_timeout_seconds", float, defaults.kinect_init_timeout_seconds)),
        kinect_retry_delay_seconds=float(_value(data, "capture", "kinect_retry_delay_seconds", float, defaults.kinect_retry_delay_seconds)),
        min_distance_m=float(_value(data, "tracking", "min_distance_m", float, defaults.min_distance_m)),
        max_distance_m=float(_value(data, "tracking", "max_distance_m", float, defaults.max_distance_m)),
        num_poses=_value(data, "tracking", "num_poses", int, defaults.num_poses),
        mirror=_value(data, "image", "mirror", bool, defaults.mirror),
        background_filter=_value(data, "image", "background_filter", bool, defaults.background_filter),
        background_max_distance_m=float(_value(data, "image", "background_max_distance_m", float, defaults.background_max_distance_m)),
        auto_center=_value(data, "image", "auto_center", bool, defaults.auto_center),
        preview=_value(data, "debug", "preview", bool, defaults.preview),
        preview_scale=float(_value(data, "debug", "preview_scale", float, defaults.preview_scale)),
    )

    if config.webcam_device < 0:
        raise ConfigError("[capture] webcam_device doit etre positif ou nul")
    if config.webcam_width <= 0 or config.webcam_height <= 0 or config.webcam_fps <= 0:
        raise ConfigError("Les dimensions et la cadence webcam doivent etre positives")
    if not 10.0 <= config.webcam_horizontal_fov_deg <= 170.0:
        raise ConfigError("[capture] webcam_horizontal_fov_deg doit etre compris entre 10 et 170")
    if config.estimated_shoulder_width_m <= 0:
        raise ConfigError("[capture] estimated_shoulder_width_m doit etre positif")
    if config.kinect_init_attempts < 1:
        raise ConfigError("[capture] kinect_init_attempts doit etre au moins 1")
    if config.kinect_init_timeout_seconds <= 0 or config.kinect_retry_delay_seconds < 0:
        raise ConfigError("Les delais d'initialisation Kinect sont invalides")
    if config.min_distance_m < 0 or config.max_distance_m <= config.min_distance_m:
        raise ConfigError("[tracking] max_distance_m doit etre superieur a min_distance_m")
    if not 1 <= config.num_poses <= 4:
        raise ConfigError("[tracking] num_poses doit etre compris entre 1 et 4")
    if config.background_max_distance_m <= 0 or config.preview_scale <= 0:
        raise ConfigError("Les distances/echelles doivent etre positives")

    return config, created
