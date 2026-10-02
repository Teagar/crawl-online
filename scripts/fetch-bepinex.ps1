[CmdletBinding()]
param(
    [ValidateSet("win-x86")]
    [string]$Target = "win-x86"
)

$ErrorActionPreference = "Stop"
$ReleaseTag = "5.4.11"
$Version = "5.4.11.0"
$Asset = "BepInEx_x86_${Version}.zip"
$Root = Split-Path -Parent $PSScriptRoot
$Destination = Join-Path $Root "artifacts/bepinex"
$Archive = Join-Path $Destination $Asset

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
& gh release download "v$ReleaseTag" --repo BepInEx/BepInEx --pattern $Asset --dir $Destination --clobber
if ($LASTEXITCODE -ne 0) {
    throw "Failed to download BepInEx $Version."
}

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $Destination "BepInEx")
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $Destination "doorstop_libs")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Destination "doorstop_config.ini")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Destination "winhttp.dll")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Destination "run_bepinex.sh")
Expand-Archive -Force -Path $Archive -DestinationPath $Destination
Write-Host "BepInEx $Version prepared at $Destination"
