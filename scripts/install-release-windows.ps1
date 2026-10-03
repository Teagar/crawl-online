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
$EntrypointConfig = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
$TemporaryDirectory = $null

function Get-Sha256([string]$Path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
function Test-LateEntrypoint {
    if (-not (Test-Path -LiteralPath $EntrypointConfig -PathType Leaf)) { return $false }
    $lines = Get-Content -LiteralPath $EntrypointConfig
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -ieq '[Preloader.Entrypoint]') { $start = $i; break } }
    if ($start -lt 0) { return $false }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^\s*\[[^]]+\]\s*$') { $end = $i; break } }
    $expected = @{ Assembly = 'Assembly-CSharp.dll'; Type = 'SystemSteam'; Method = 'Awake' }
    foreach ($key in $expected.Keys) {
        $found = $false
        for ($i = $start + 1; $i -lt $end; $i++) {
            if ($lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=\s*(.*?)\s*$')) { $found = $Matches[1] -ceq $expected[$key]; break }
        }
        if (-not $found) { return $false }
    }
    return $true
}
function Set-LateEntrypoint {
    $directory = Split-Path -Parent $EntrypointConfig
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $lines = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $EntrypointConfig) { foreach ($line in (Get-Content -LiteralPath $EntrypointConfig)) { $lines.Add($line) } }
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -ieq '[Preloader.Entrypoint]') { $start = $i; break } }
    if ($start -lt 0) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1].Trim()) { $lines.Add('') }
        $lines.Add('[Preloader.Entrypoint]'); $start = $lines.Count - 1
    }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^\s*\[[^]]+\]\s*$') { $end = $i; break } }
    $expected = [ordered]@{ Assembly = 'Assembly-CSharp.dll'; Type = 'SystemSteam'; Method = 'Awake' }
    foreach ($key in $expected.Keys) {
        $found = $false
        for ($i = $start + 1; $i -lt $end; $i++) {
            if ($lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $lines[$i] = "$key = $($expected[$key])"; $found = $true; break }
        }
        if (-not $found) { $lines.Insert($end, "$key = $($expected[$key])"); $end++ }
    }
    [IO.File]::WriteAllLines($EntrypointConfig, $lines, [Text.UTF8Encoding]::new($false))
}
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
    Write-Host 'Crawl.exe architecture: Windows x86 (PE machine 0x014c)'
    $hash = Get-Sha256 $assembly
    Write-Host "Crawl Assembly-CSharp.dll SHA-256: $hash"
    if ($hash -ne $KnownAssemblyHash -and -not $AllowUnknownGame) { throw 'Unsupported Crawl Windows build. Re-run only after reviewing with -AllowUnknownGame.' }
    Write-Host "Crawl Windows build: $(if ($hash -eq $KnownAssemblyHash) { 'supported 1.0.1' } else { 'unknown (override enabled)' })"
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
        $isInstalled = Test-Path -LiteralPath $State
        if ($isInstalled) {
            $installed = Get-Content -Raw -LiteralPath $State | ConvertFrom-Json
            Write-Host "Crawl Online version: $($installed.version)"
            $failed = $false
            foreach ($property in $installed.plugins.psobject.Properties) {
                $file = Join-Path $PluginDir $property.Name
                $ok = (Test-Path -LiteralPath $file -PathType Leaf) -and ((Get-Sha256 $file) -eq $property.Value)
                Write-Host "$($property.Name): $(if ($ok) { 'verified' } else { 'MISSING OR MODIFIED' })"
                if (-not $ok) { $failed = $true }
            }
            if ($failed) { throw 'Crawl Online plugin integrity check failed.' }
        } else { Write-Host 'Crawl Online is not installed.' }
        $core = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
        if (Test-Path -LiteralPath $core) {
            $coreVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($core).FileVersion
            Write-Host "BepInEx core: present$(if ($coreVersion) { " ($coreVersion)" } else { '' })"
        } else { Write-Host 'BepInEx core: missing' }
        Write-Host "Windows x86 Doorstop (winhttp.dll): $(if (Test-Path -LiteralPath (Join-Path $GameDir 'winhttp.dll')) { 'present' } else { 'missing' })"
        $lateEntrypoint = Test-LateEntrypoint
        Write-Host "BepInEx late Crawl entrypoint: $(if ($lateEntrypoint) { 'configured' } else { 'MISSING OR INCORRECT' })"
        $log = Join-Path $GameDir 'BepInEx\LogOutput.log'
        Write-Host "BepInEx log: $(if (Test-Path -LiteralPath $log) { 'present; use collect-diagnostics-windows.ps1 for a sanitized archive' } else { 'missing (launch Crawl once through Steam)' })"
        if ($isInstalled -and -not $lateEntrypoint) { throw 'Installed Crawl Online has an incomplete or incompatible BepInEx loader.' }
        exit 0
    }
    $ReleaseDir = Get-ReleaseDirectory $Package
    $Manifest = Get-Manifest $ReleaseDir
    $Core = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
    if (-not (Test-Path -LiteralPath $Core)) {
        $entry = $Manifest.bepInEx.'win-x86'
        if (-not $entry.url -or $entry.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Release manifest lacks a valid BepInEx x86 hash.' }
        if (-not $TemporaryDirectory) { $TemporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("crawl-online-" + [guid]::NewGuid()); New-Item -ItemType Directory -Path $TemporaryDirectory | Out-Null }
        $archive = Join-Path $TemporaryDirectory 'bepinex.zip'
        Invoke-WebRequest -Uri $entry.url -OutFile $archive
        if ((Get-Sha256 $archive) -ne $entry.sha256) { throw 'BepInEx download hash mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $GameDir -Force
    } elseif (([Diagnostics.FileVersionInfo]::GetVersionInfo($Core).FileVersion -notmatch '5\.4\.11')) {
        throw 'Existing BepInEx is not 5.4.11; it was not changed.'
    }
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
    $stage = Join-Path $PluginDir ('.crawl-online-stage-' + [guid]::NewGuid())
    $backup = Join-Path $PluginDir ('.crawl-online-backup-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $stage, $backup | Out-Null
    $names = @('CrawlOnline.dll', 'CrawlOnline.Runtime.dll', '.crawl-online-install.json')
    $activationStarted = $false
    $entrypointChanged = $false
    $entrypointExisted = Test-Path -LiteralPath $EntrypointConfig
    $entrypointBackup = if ($entrypointExisted) { [IO.File]::ReadAllBytes($EntrypointConfig) } else { $null }
    try {
        Set-LateEntrypoint
        $entrypointChanged = $true
        if (-not (Test-LateEntrypoint)) { throw 'Could not configure the required late Crawl BepInEx entrypoint.' }
        Copy-Item -Force (Join-Path $ReleaseDir 'plugins\CrawlOnline.dll'), (Join-Path $ReleaseDir 'plugins\CrawlOnline.Runtime.dll') -Destination $stage
        @{ version = $Manifest.version; plugins = $Manifest.plugins } | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $stage '.crawl-online-install.json')
        $activationStarted = $true
        foreach ($name in $names) { $current = Join-Path $PluginDir $name; if (Test-Path -LiteralPath $current) { Move-Item -LiteralPath $current -Destination (Join-Path $backup $name) } }
        $activated = 0
        foreach ($name in $names) {
            Move-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $PluginDir $name)
            $activated++
            if ($env:CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE -eq '1' -and $activated -eq 1) { throw 'Injected activation failure for rollback test.' }
        }
    } catch {
        if ($entrypointChanged) {
            if ($entrypointExisted) { [IO.File]::WriteAllBytes($EntrypointConfig, $entrypointBackup) } else { Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $EntrypointConfig }
        }
        if ($activationStarted) {
            foreach ($name in $names) { Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath (Join-Path $PluginDir $name); $old = Join-Path $backup $name; if (Test-Path -LiteralPath $old) { Move-Item -LiteralPath $old -Destination (Join-Path $PluginDir $name) } }
        }
        throw
    } finally {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -LiteralPath $stage, $backup
    }
    Write-Host 'Crawl Online installed. Start Crawl normally through Steam.'
} finally {
    if ($TemporaryDirectory -and (Test-Path -LiteralPath $TemporaryDirectory)) { Remove-Item -Recurse -Force -LiteralPath $TemporaryDirectory }
}
