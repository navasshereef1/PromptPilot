# Installs or updates PromptPilot for the current user. No admin rights needed.
# Downloads the latest release from GitHub, checks its SHA-256, and starts it.
# Read it first if you like: https://github.com/navasshereef1/PromptPilot/blob/main/install.ps1
param(
    [string]$Dir = "$env:LOCALAPPDATA\Programs\PromptPilot",
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

$base = 'https://github.com/navasshereef1/PromptPilot/releases/latest/download'
$temp = Join-Path ([IO.Path]::GetTempPath()) ("promptpilot-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
try {
    Write-Host 'Downloading PromptPilot...'
    Invoke-WebRequest "$base/PromptPilot.exe" -OutFile "$temp\PromptPilot.exe" -UseBasicParsing
    Invoke-WebRequest "$base/PromptPilot.exe.sha256" -OutFile "$temp\PromptPilot.exe.sha256" -UseBasicParsing

    $expected = ((Get-Content "$temp\PromptPilot.exe.sha256" -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash "$temp\PromptPilot.exe" -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "Checksum mismatch, so nothing was installed. Expected $expected, got $actual." }

    New-Item -ItemType Directory -Force $Dir | Out-Null
    Get-Process PromptPilot -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq "$Dir\PromptPilot.exe" } | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Move-Item "$temp\PromptPilot.exe" "$Dir\PromptPilot.exe" -Force
    Write-Host "Installed to $Dir"
    if (-not $NoStart) { Start-Process "$Dir\PromptPilot.exe" }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
