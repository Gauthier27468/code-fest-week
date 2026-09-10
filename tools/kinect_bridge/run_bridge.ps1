#!/usr/bin/env pwsh
# Lance le bridge sous Windows avec l'environnement gere par uv.
# Le mode webcam est entierement portable. Le mode Kinect Windows reste volontairement
# unsupported ici : il necessite un binding freenect natif construit pour la machine.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Error "uv est introuvable. Installe-le puis relance ce script."
    exit 1
}

uv sync --quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$VenvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $VenvPython)) {
    Write-Error "L'environnement uv n'a pas cree $VenvPython."
    exit 1
}

if (-not (Test-Path "models\pose_landmarker_lite.task")) {
    Write-Host "Téléchargement du modèle PoseLandmarker (lite)..."
    New-Item -ItemType Directory -Force -Path models | Out-Null
    Invoke-WebRequest -Uri "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task" `
        -OutFile "models\pose_landmarker_lite.task"
}

& $VenvPython -m kibird_bridge.bridge @args
exit $LASTEXITCODE
