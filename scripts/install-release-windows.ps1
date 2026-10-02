[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('install', 'update', 'uninstall', 'diagnose')]
    [string]$Command,
    [string]$Package,
    [string]$GameDir = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Crawl",
    [switch]$AllowUnknownGame
)

$ErrorActionPreference = 'Stop'
$KnownAssemblyHash = 'e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e'
$PluginDir = Join-Path $GameDir 'BepInEx\plugins\CrawlOnline'
$State = Join-Path $PluginDir '.crawl-online-install.json'
$TemporaryDirectory = $null

function Get-Sha256([string]$Path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
function Get-ReleaseDirectory([string]$Path) {
    if (-not $Path) { throw 'A release package is required for install or update.' }
    if (Test-Path -LiteralPath $Path -PathType Container) { return (Resolve-Path -LiteralPath $Path).Path }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Package not found: $Path" }
    $script:TemporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("crawl-online-" + [guid]::NewGuid())
    Expand-Archive -LiteralPath $Path -DestinationPath $script:TemporaryDirectory -Force
    $manifest = Get-ChildItem -LiteralPath $script:TemporaryDirectory -Filter 'CrawlOnline.release.json' -Recurse | Select-Object -First 1
    if (-not $manifest) { throw 'Package does not contain CrawlOnline.release.json.' }
    return $manifest.Directory.FullName
}
function Test-Game {
    $exe = Join-Path $GameDir 'Crawl.exe'
    $assembly = Join-Path $GameDir 'Crawl_Data\Managed\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $assembly)) { throw "Crawl Windows was not found at $GameDir. Pass -GameDir." }
    # PE Machine 0x14c is x86. This prevents installing the required x86 loader into an unexpected executable.
    $stream = [IO.File]::OpenRead($exe); try { $reader = New-Object IO.BinaryReader($stream); $stream.Position = 0x3c; $offset = $reader.ReadInt32(); $stream.Position = $offset + 4; if ($reader.ReadUInt16() -ne 0x14c) { throw 'Crawl.exe is not an x86 executable; nothing was changed.' } } finally { $stream.Dispose() }
    $hash = Get-Sha256 $assembly
    Write-Host "Crawl Assembly-CSharp.dll SHA-256: $hash"
    if ($hash -ne $KnownAssemblyHash -and -not $AllowUnknownGame) { throw 'Unsupported Crawl Windows build. Re-run only after reviewing with -AllowUnknownGame.' }
}
function Get-Manifest([string]$ReleaseDir) {
    $manifestPath = Join-Path $ReleaseDir 'CrawlOnline.release.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Release manifest is missing.' }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or -not $manifest.version) { throw 'Unsupported release manifest.' }
    foreach ($property in $manifest.plugins.psobject.Properties) {
        $file = Join-Path $ReleaseDir (Join-Path 'plugins' $property.Name)
        if (-not (Test-Path -LiteralPath $file) -or (Get-Sha256 $file) -ne $property.Value) { throw "Package hash mismatch: $($property.Name)" }
    }
    Write-Host "Release $($manifest.version) package hashes verified."
    return $manifest
}
try {
    Test-Game
    if ($Command -eq 'uninstall') {
        Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $PluginDir 'CrawlOnline.dll'), (Join-Path $PluginDir 'CrawlOnline.Runtime.dll'), $State
        if ((Test-Path -LiteralPath $PluginDir) -and -not (Get-ChildItem -Force -LiteralPath $PluginDir)) { Remove-Item -Force -LiteralPath $PluginDir }
        Write-Host 'Crawl Online removed. BepInEx and every other plugin were preserved.'
        exit 0
    }
    if ($Command -eq 'diagnose') {
        if (Test-Path -LiteralPath $State) { Get-Content -Raw -LiteralPath $State } else { Write-Host 'Crawl Online is not installed.' }
        if (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) { Write-Host 'BepInEx core: present' } else { Write-Host 'BepInEx core: missing' }
        exit 0
    }
    $ReleaseDir = Get-ReleaseDirectory $Package
    $Manifest = Get-Manifest $ReleaseDir
    $Core = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
    if (-not (Test-Path -LiteralPath $Core)) {
        $entry = $Manifest.bepInEx.'win-x86'
        if (-not $entry.url -or $entry.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Release manifest lacks a valid BepInEx x86 hash.' }
        $archive = Join-Path $TemporaryDirectory 'bepinex.zip'
        Invoke-WebRequest -Uri $entry.url -OutFile $archive
        if ((Get-Sha256 $archive) -ne $entry.sha256) { throw 'BepInEx download hash mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $GameDir -Force
    } elseif (([Diagnostics.FileVersionInfo]::GetVersionInfo($Core).FileVersion -notmatch '5\.4\.11')) {
        throw 'Existing BepInEx is not 5.4.11; it was not changed.'
    }
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
    Copy-Item -Force (Join-Path $ReleaseDir 'plugins\CrawlOnline.dll'), (Join-Path $ReleaseDir 'plugins\CrawlOnline.Runtime.dll') -Destination $PluginDir
    @{ version = $Manifest.version; plugins = $Manifest.plugins } | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath $State
    Write-Host 'Crawl Online installed. Start Crawl normally through Steam.'
} finally {
    if ($TemporaryDirectory -and (Test-Path -LiteralPath $TemporaryDirectory)) { Remove-Item -Recurse -Force -LiteralPath $TemporaryDirectory }
}
