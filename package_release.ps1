$ErrorActionPreference = "Stop"

$version = "v1.0.0"
$distDir = "dist\LocalVoice-$version"
$zipPath = "dist\LocalVoice-$version.zip"

Write-Host "Creating release package for LocalVoice $version..."

if (Test-Path "dist") {
    Remove-Item "dist" -Recurse -Force
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path "$distDir\engine" -Force | Out-Null

# 1. Copy published WPF application
Copy-Item "publish\LocalVoice\*" -Destination $distDir -Recurse -Force

# 2. Copy Python engine files (excluding venv to keep release lightweight)
$engineFiles = @("audio_capture.py", "config.py", "engine_host.py", "transcriber.py", "vad_chunker.py", "requirements.txt", "silero_vad.onnx", "setup_environment.bat")
foreach ($file in $engineFiles) {
    if (Test-Path "engine\$file") {
        Copy-Item "engine\$file" -Destination "$distDir\engine\$file" -Force
    }
}

# 3. Copy Documentation
Copy-Item "README.md" -Destination "$distDir\README.md" -Force

# 4. Create zip archive
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -Force

Write-Host "Successfully packaged release archive: $zipPath"
Get-Item $zipPath | Select-Object Name, Length, LastWriteTime
