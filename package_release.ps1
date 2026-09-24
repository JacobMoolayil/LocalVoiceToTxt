param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

if (-not $Version) {
    if ($env:GITHUB_REF_NAME -and $env:GITHUB_REF_NAME -ne "master") {
        $Version = $env:GITHUB_REF_NAME
    } else {
        $Version = "v1.1.0"
    }
}

if (-not $Version.StartsWith("v")) {
    $Version = "v$Version"
}

$distDir = "dist\LocalVoice-$Version"
$zipPath = "dist\LocalVoice-$Version.zip"

Write-Host "Creating release package for LocalVoice $Version..."

if (Test-Path "dist") {
    Remove-Item "dist" -Recurse -Force
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path "$distDir\engine" -Force | Out-Null

# 1. Copy published WPF application
Copy-Item "publish\LocalVoice\*" -Destination $distDir -Recurse -Force

# 2. Ensure Silero VAD model is present
$sileroPath = "engine\silero_vad.onnx"
if (-not (Test-Path $sileroPath)) {
    Write-Host "Downloading Silero VAD model..."
    Invoke-WebRequest -Uri "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx" -OutFile $sileroPath
}

# 3. Copy Python engine files (excluding venv to keep release lightweight)
$engineFiles = @("audio_capture.py", "config.py", "engine_host.py", "transcriber.py", "vad_chunker.py", "requirements.txt", "silero_vad.onnx", "setup_environment.bat")
foreach ($file in $engineFiles) {
    if (Test-Path "engine\$file") {
        Copy-Item "engine\$file" -Destination "$distDir\engine\$file" -Force
    }
}

# 4. Copy Documentation & Legal
Copy-Item "README.md" -Destination "$distDir\README.md" -Force
if (Test-Path "LICENSE") { Copy-Item "LICENSE" -Destination "$distDir\LICENSE" -Force }
if (Test-Path "ai.txt") { Copy-Item "ai.txt" -Destination "$distDir\ai.txt" -Force }

# 5. Create zip archive
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -Force

Write-Host "Successfully packaged release archive: $zipPath"
Get-Item $zipPath | Select-Object Name, Length, LastWriteTime
