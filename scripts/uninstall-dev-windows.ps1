[CmdletBinding()]
param(
    [string]$GameDir = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Crawl"
)

$ErrorActionPreference = "Stop"
$PluginDir = Join-Path $GameDir "BepInEx/plugins/CrawlOnline"

Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $PluginDir "CrawlOnline.dll")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $PluginDir "CrawlOnline.Runtime.dll")
if ((Test-Path $PluginDir) -and -not (Get-ChildItem -Force $PluginDir)) {
    Remove-Item -Force $PluginDir
}

Write-Host "Crawl Online removed. BepInEx was preserved to avoid deleting other mods."
