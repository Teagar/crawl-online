[CmdletBinding()]
param(
    [string]$GameDir = "${env:ProgramFiles(x86)}\Steam\steamapps\common\Crawl",
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$KnownAssemblyHash = 'e93e8fb49fd3c3ebe622d0f9f9557c1e4dd475c2a277be19e2c05cbb1f05f61e'
$PluginDir = Join-Path $GameDir 'BepInEx\plugins\CrawlOnline'
$StatePath = Join-Path $PluginDir '.crawl-online-install.json'
$ConfigPath = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
$LogPath = Join-Path $GameDir 'BepInEx\LogOutput.log'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-PeMachine([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'missing' }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return 'not-pe' }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or ($offset + 6) -gt $stream.Length) { return 'invalid-pe' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 'invalid-pe' }
        $machine = $reader.ReadUInt16()
        if ($machine -eq 0x14c) { return 'x86 (0x014c)' }
        return ('other (0x{0:x4})' -f $machine)
    } finally {
        $stream.Dispose()
    }
}

function Test-LateEntrypoint {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $false }
    $text = Get-Content -Raw -LiteralPath $ConfigPath
    $section = [regex]::Match($text, '(?ms)^\s*\[Preloader\.Entrypoint\]\s*$.*?(?=^\s*\[|\z)')
    if (-not $section.Success) { return $false }
    return $section.Value -match '(?mi)^\s*Assembly\s*=\s*Assembly-CSharp\.dll\s*$' -and
        $section.Value -match '(?mi)^\s*Type\s*=\s*SystemSteam\s*$' -and
        $section.Value -match '(?mi)^\s*Method\s*=\s*Awake\s*$'
}

function Protect-DiagnosticText([string]$Text) {
    if ($null -eq $Text) { return '' }
    if ($env:USERPROFILE) {
        $Text = [regex]::Replace($Text, [regex]::Escape($env:USERPROFILE), '%USERPROFILE%', 'IgnoreCase')
    }
    $Text = [regex]::Replace($Text, '(?i)[a-z]:\\Users\\[^\\\r\n]+', '%USERPROFILE%')
    $Text = [regex]::Replace($Text, '(?<!\d)\d{16,20}(?!\d)', '<redacted-long-id>')
    $Text = [regex]::Replace($Text, '(?i)\b(token|password|secret|authorization)\s*[:=]\s*\S+', '$1=<redacted>')
    $Text = [regex]::Replace($Text, '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?!\d)', '<redacted-address>')
    return $Text
}

if (-not $OutputPath) {
    $OutputPath = Join-Path (Get-Location) ('CrawlOnline-Windows-diagnostics-{0}.zip' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($OutputPath) -ne '.zip') { throw 'OutputPath must end in .zip.' }

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('crawl-online-diagnostics-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $report = New-Object 'Collections.Generic.List[string]'
    $report.Add('Crawl Online Windows diagnostic report')
    $report.Add(('generatedUtc={0}' -f [DateTime]::UtcNow.ToString('o')))
    $report.Add(('osVersion={0}' -f [Environment]::OSVersion.VersionString))
    $report.Add(('powershellVersion={0}' -f $PSVersionTable.PSVersion))
    $report.Add('expectedGame=Crawl Windows 1.0.1 x86')

    $exe = Join-Path $GameDir 'Crawl.exe'
    $assembly = Join-Path $GameDir 'Crawl_Data\Managed\Assembly-CSharp.dll'
    $machine = Get-PeMachine $exe
    $report.Add(('crawlExe={0}' -f $(if (Test-Path -LiteralPath $exe -PathType Leaf) { 'present' } else { 'missing' })))
    $report.Add(('crawlExeMachine={0}' -f $machine))
    if (Test-Path -LiteralPath $assembly -PathType Leaf) {
        $assemblyHash = Get-Sha256 $assembly
        $report.Add(('assemblySha256={0}' -f $assemblyHash))
        $report.Add(('assemblyBuild={0}' -f $(if ($assemblyHash -eq $KnownAssemblyHash) { 'supported-windows-1.0.1' } else { 'unknown' })))
    } else {
        $report.Add('assemblySha256=missing')
        $report.Add('assemblyBuild=missing')
    }

    $core = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
    $doorstop = Join-Path $GameDir 'winhttp.dll'
    $report.Add(('doorstopWinhttp={0}' -f $(if (Test-Path -LiteralPath $doorstop -PathType Leaf) { 'present' } else { 'missing' })))
    if (Test-Path -LiteralPath $core -PathType Leaf) {
        $coreVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($core).FileVersion
        $report.Add(('bepInExCore=present'))
        $report.Add(('bepInExVersion={0}' -f $(if ($coreVersion) { $coreVersion } else { 'unknown' })))
    } else {
        $report.Add('bepInExCore=missing')
        $report.Add('bepInExVersion=missing')
    }
    $report.Add(('lateEntrypoint={0}' -f $(if (Test-LateEntrypoint) { 'configured' } else { 'missing-or-incorrect' })))

    if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
        try {
            $state = Get-Content -Raw -LiteralPath $StatePath | ConvertFrom-Json
            $report.Add(('crawlOnlineState=present'))
            $report.Add(('crawlOnlineVersion={0}' -f $state.version))
            foreach ($property in $state.plugins.psobject.Properties) {
                $pluginPath = Join-Path $PluginDir $property.Name
                $integrity = if ((Test-Path -LiteralPath $pluginPath -PathType Leaf) -and (Get-Sha256 $pluginPath) -eq $property.Value) { 'verified' } else { 'missing-or-modified' }
                $report.Add(('plugin.{0}={1}' -f $property.Name, $integrity))
            }
        } catch {
            $report.Add('crawlOnlineState=invalid')
        }
    } else {
        $report.Add('crawlOnlineState=missing')
    }

    if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
        $log = (Get-Content -LiteralPath $LogPath -Tail 600) -join [Environment]::NewLine
        $safeLog = Protect-DiagnosticText $log
        [IO.File]::WriteAllText((Join-Path $temporary 'sanitized-bepinex.log'), $safeLog, (New-Object Text.UTF8Encoding($false)))
        $report.Add('sanitizedBepInExLog=included-last-600-lines')
    } else {
        $report.Add('sanitizedBepInExLog=missing')
    }

    $safeReport = Protect-DiagnosticText ($report -join [Environment]::NewLine)
    [IO.File]::WriteAllText((Join-Path $temporary 'report.txt'), $safeReport + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    $notice = @'
This archive contains only generated text and a sanitized BepInEx log excerpt.
It intentionally excludes Crawl/Steam binaries, saves, raw configuration, credentials,
absolute user paths, Steam/lobby IDs, and network addresses. Review it before sharing.
'@
    [IO.File]::WriteAllText((Join-Path $temporary 'PRIVACY.txt'), $notice.Trim() + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))

    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $OutputPath
    Compress-Archive -Path (Join-Path $temporary '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Diagnostic archive created: $OutputPath"
    Write-Host 'Review report.txt and sanitized-bepinex.log before sharing.'
} finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -LiteralPath $temporary
}
