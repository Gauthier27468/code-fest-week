#!/usr/bin/env pwsh
# Équivalent Windows de run_bridge.sh : lance le bridge Kinect -> Unity avec un
# environnement uv prêt à l'emploi.
#
# `freenect` (bindings libfreenect) n'existe pas sur PyPI. Sous Linux, run_bridge.sh
# s'appuie sur --system-site-packages pour y accéder depuis le Python système. Sous
# Windows, ce module a été construit depuis les sources (voir tools/kinect_bridge/README.md)
# et installé dans le site-packages "utilisateur" du Python système ($FreenectUserSite
# ci-dessous), qu'un venv --system-site-packages n'expose pas de façon fiable. Ce script
# copie donc freenect.pyd et ses DLL directement dans le site-packages du venv après coup.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$Venv = ".venv"
$SystemPython = "C:\Python313\python.exe"
$FreenectUserSite = Join-Path $env:APPDATA "Python\Python313\site-packages"
$VenvPython = Join-Path $Venv "Scripts\python.exe"
$VenvSitePackages = Join-Path $Venv "Lib\site-packages"

if (-not (Test-Path $Venv)) {
    if (-not (Test-Path $SystemPython)) {
        Write-Error "Python système introuvable : $SystemPython (ajuste `$SystemPython dans ce script si besoin)."
        exit 1
    }

    Write-Host "Création du venv..."
    uv venv --python $SystemPython $Venv
    uv pip install --python $VenvPython -r requirements.txt
}

$FreenectFiles = "freenect.pyd", "libfreenect.dll", "libfreenect_sync.dll", "libusb-1.0.dll", "libwinpthread-1.dll"

if (-not (Test-Path (Join-Path $FreenectUserSite "freenect.pyd"))) {
    Write-Error ("freenect introuvable dans $FreenectUserSite . " +
        "Construis-le depuis les sources (voir tools/kinect_bridge/README.md) avant de relancer ce script.")
    exit 1
}

# Toujours resynchroniser (pas seulement si absent) : sinon un rebuild de libfreenect
# après la création du venv laisse une DLL périmée dans le venv sans avertissement.
$NeedsCopy = $false
foreach ($f in $FreenectFiles) {
    $src = Join-Path $FreenectUserSite $f
    $dst = Join-Path $VenvSitePackages $f
    if (-not (Test-Path $dst) -or (Get-Item $src).LastWriteTimeUtc -gt (Get-Item $dst).LastWriteTimeUtc) {
        $NeedsCopy = $true
        break
    }
}
if ($NeedsCopy) {
    Write-Host "Copie/mise à jour des bindings freenect (libfreenect, libusb, winpthread) dans le venv..."
    foreach ($f in $FreenectFiles) {
        Copy-Item (Join-Path $FreenectUserSite $f) $VenvSitePackages -Force
    }
}

if (-not (Test-Path "models\pose_landmarker_lite.task")) {
    Write-Host "Téléchargement du modèle PoseLandmarker (lite)..."
    New-Item -ItemType Directory -Force -Path models | Out-Null
    Invoke-WebRequest -Uri "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task" `
        -OutFile "models\pose_landmarker_lite.task"
}

& $VenvPython -m kibird_bridge.bridge @args
