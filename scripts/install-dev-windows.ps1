[CmdletBinding()]
param(
    [string]$GameDir = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Crawl"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Loader = Join-Path $Root "artifacts/bepinex"
$Plugin = Join-Path $Root "src/CrawlOnline.Bootstrap/bin/Release/net35/CrawlOnline.dll"
$Runtime = Join-Path $Root "src/CrawlOnline/bin/Release/net35/CrawlOnline.Runtime.dll"
$PluginDir = Join-Path $GameDir "BepInEx/plugins/CrawlOnline"
$ConfigDir = Join-Path $GameDir "BepInEx/config"

if (-not (Test-Path (Join-Path $GameDir "Crawl.exe"))) {
    throw "Crawl was not found at $GameDir. Pass -GameDir with the Steam installation path."
}
if (-not (Test-Path (Join-Path $Loader "BepInEx/core/BepInEx.dll"))) {
    throw "Run scripts/fetch-bepinex.ps1 first."
}
if (-not (Test-Path $Plugin) -or -not (Test-Path $Runtime)) {
    throw "Build the solution in Release mode before installing."
}

New-Item -ItemType Directory -Force -Path $PluginDir, $ConfigDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $Loader "BepInEx/core") (Join-Path $GameDir "BepInEx")
Copy-Item -Force (Join-Path $Root "packaging/common/BepInEx.cfg") (Join-Path $ConfigDir "BepInEx.cfg")
Copy-Item -Force (Join-Path $Loader "doorstop_config.ini") $GameDir
Copy-Item -Force (Join-Path $Loader "winhttp.dll") $GameDir
Copy-Item -Force $Plugin, $Runtime $PluginDir

Write-Host "Crawl Online development build installed. Start Crawl normally through Steam."
