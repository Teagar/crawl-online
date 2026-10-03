$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Collector = Join-Path $Root 'scripts\collect-diagnostics-windows.ps1'
$Temporary = Join-Path ([IO.Path]::GetTempPath()) ('crawl-online-diagnostic-test-' + [guid]::NewGuid())
$Game = Join-Path $Temporary 'game'
$Output = Join-Path $Temporary 'diagnostics.zip'
$Expanded = Join-Path $Temporary 'expanded'

function Write-PeX86([string]$Path) {
    $data = New-Object byte[] 512
    $data[0] = 0x4d; $data[1] = 0x5a
    [BitConverter]::GetBytes([int]128).CopyTo($data, 0x3c)
    $data[128] = 0x50; $data[129] = 0x45
    [BitConverter]::GetBytes([uint16]0x14c).CopyTo($data, 132)
    [IO.File]::WriteAllBytes($Path, $data)
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

New-Item -ItemType Directory -Force -Path `
    (Join-Path $Game 'Crawl_Data\Managed'), `
    (Join-Path $Game 'BepInEx\core'), `
    (Join-Path $Game 'BepInEx\config'), `
    (Join-Path $Game 'BepInEx\plugins\CrawlOnline') | Out-Null

try {
    Write-PeX86 (Join-Path $Game 'Crawl.exe')
    [IO.File]::WriteAllText((Join-Path $Game 'Crawl_Data\Managed\Assembly-CSharp.dll'), 'fixture only')
    [IO.File]::WriteAllText((Join-Path $Game 'BepInEx\core\BepInEx.dll'), 'fixture only')
    [IO.File]::WriteAllText((Join-Path $Game 'winhttp.dll'), 'fixture only')
    [IO.File]::WriteAllText((Join-Path $Game 'BepInEx\config\BepInEx.cfg'), @'
[Preloader.Entrypoint]
Assembly = Assembly-CSharp.dll
Type = SystemSteam
Method = Awake
'@)
    $plugin = Join-Path $Game 'BepInEx\plugins\CrawlOnline\CrawlOnline.dll'
    [IO.File]::WriteAllText($plugin, 'plugin fixture')
    $pluginHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $plugin).Hash.ToLowerInvariant()
    @{ version = 'test'; plugins = @{ 'CrawlOnline.dll' = $pluginHash } } |
        ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $Game 'BepInEx\plugins\CrawlOnline\.crawl-online-install.json')
    [IO.File]::WriteAllText((Join-Path $Game 'BepInEx\LogOutput.log'), @'
User path C:\Users\PrivateName\Steam
Peer 76561198000000001 lobby 109775243858300419 address 192.168.1.20:27015
token=should-never-leak
'@)

    & $Collector -GameDir $Game -OutputPath $Output
    Expand-Archive -LiteralPath $Output -DestinationPath $Expanded
    $report = Get-Content -Raw -LiteralPath (Join-Path $Expanded 'report.txt')
    $log = Get-Content -Raw -LiteralPath (Join-Path $Expanded 'sanitized-bepinex.log')
    Assert-True ($report -match 'crawlExeMachine=x86 \(0x014c\)') 'x86 PE was not identified.'
    Assert-True ($report -match 'lateEntrypoint=configured') 'Late entrypoint was not identified.'
    Assert-True ($report -match 'plugin.CrawlOnline.dll=verified') 'Plugin integrity was not verified.'
    Assert-True ($log -notmatch 'PrivateName|76561198000000001|109775243858300419|192\.168\.1\.20|should-never-leak') 'Sensitive fixture data was not redacted.'
    Assert-True ($log -match '<redacted-long-id>|<redacted-address>|<redacted>') 'Redaction markers are missing.'
    $unsafe = Get-ChildItem -File -Recurse -LiteralPath $Expanded | Where-Object { $_.Extension -in @('.dll', '.exe', '.sav') }
    Assert-True (-not $unsafe) 'The archive contains a forbidden binary or save.'
    Write-Host 'Windows diagnostic collector fixture test passed'
} finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -LiteralPath $Temporary
}
