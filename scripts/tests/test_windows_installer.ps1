$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Installer = Join-Path $Root 'scripts\install-release-windows.ps1'
$Temporary = Join-Path ([IO.Path]::GetTempPath()) ('crawl-online-windows-installer-test-' + [guid]::NewGuid())
$Game = Join-Path $Temporary 'game'
$Package = Join-Path $Temporary 'package'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Write-PeX86([string]$Path) {
    $data = New-Object byte[] 512
    $data[0] = 0x4d; $data[1] = 0x5a
    [BitConverter]::GetBytes([int]128).CopyTo($data, 0x3c)
    $data[128] = 0x50; $data[129] = 0x45
    [BitConverter]::GetBytes([uint16]0x14c).CopyTo($data, 132)
    [IO.File]::WriteAllBytes($Path, $data)
}

function Invoke-Installer([string]$Command, [string[]]$Extra, [int]$ExpectedExit = 0) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Installer, $Command) + $Extra
    $output = & powershell.exe @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExit) {
        throw "Installer $Command exited $exitCode instead of $ExpectedExit`n$($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine)
}

New-Item -ItemType Directory -Force -Path `
    (Join-Path $Game 'Crawl_Data\Managed'), `
    (Join-Path $Game 'BepInEx\core'), `
    (Join-Path $Game 'BepInEx\config'), `
    (Join-Path $Game 'BepInEx\plugins\OtherMod'), `
    (Join-Path $Package 'plugins') | Out-Null

try {
    Write-PeX86 (Join-Path $Game 'Crawl.exe')
    [IO.File]::WriteAllText((Join-Path $Game 'Crawl_Data\Managed\Assembly-CSharp.dll'), 'legitimate fixture substitute')
    [IO.File]::WriteAllText((Join-Path $Game 'winhttp.dll'), 'x86 Doorstop fixture')
    [IO.File]::WriteAllText((Join-Path $Game 'BepInEx\plugins\OtherMod\keep.txt'), 'keep')
    [IO.File]::WriteAllText((Join-Path $Game 'BepInEx\config\BepInEx.cfg'), @'
[Preloader.Entrypoint]
Assembly = UnityEngine.dll
Type = Application
Method = .cctor
'@)
    Add-Type -Language CSharp -OutputAssembly (Join-Path $Game 'BepInEx\core\BepInEx.dll') -TypeDefinition @'
using System.Reflection;
[assembly: AssemblyVersion("5.4.11.0")]
[assembly: AssemblyFileVersion("5.4.11.0")]
public sealed class BepInExFixture { }
'@

    $bootstrap = Join-Path $Package 'plugins\CrawlOnline.dll'
    $runtime = Join-Path $Package 'plugins\CrawlOnline.Runtime.dll'
    [IO.File]::WriteAllText($bootstrap, 'new bootstrap')
    [IO.File]::WriteAllText($runtime, 'new runtime')
    $manifest = @{
        schemaVersion = 1
        version = 'test-1'
        plugins = @{
            'CrawlOnline.dll' = (Get-FileHash -Algorithm SHA256 -LiteralPath $bootstrap).Hash.ToLowerInvariant()
            'CrawlOnline.Runtime.dll' = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtime).Hash.ToLowerInvariant()
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $Package 'CrawlOnline.release.json')
    $env:CRAWL_ONLINE_TEST_ALLOW_UNKNOWN_GAME = '1'
    $common = @('-Package', $Package, '-GameDir', $Game)

    Invoke-Installer 'install' $common | Out-Null
    $pluginDir = Join-Path $Game 'BepInEx\plugins\CrawlOnline'
    $installedBootstrap = Join-Path $pluginDir 'CrawlOnline.dll'
    $installedRuntime = Join-Path $pluginDir 'CrawlOnline.Runtime.dll'
    $statePath = Join-Path $pluginDir '.crawl-online-install.json'
    Assert-True ((Get-Content -Raw -LiteralPath $installedBootstrap) -eq 'new bootstrap') 'Clean install did not activate the bootstrap.'
    Assert-True ((Get-Content -Raw -LiteralPath $installedRuntime) -eq 'new runtime') 'Clean install did not activate the runtime.'

    [IO.File]::WriteAllText($installedBootstrap, 'old bootstrap')
    [IO.File]::WriteAllText($installedRuntime, 'old runtime')
    @{ version = 'old'; plugins = @{} } | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath $statePath
    Invoke-Installer 'update' $common | Out-Null
    Assert-True ((Get-Content -Raw -LiteralPath $installedBootstrap) -eq 'new bootstrap') 'Update did not replace the old bootstrap.'
    Assert-True ((Get-Content -Raw -LiteralPath $installedRuntime) -eq 'new runtime') 'Update did not replace the old runtime.'

    $beforeBootstrap = [IO.File]::ReadAllBytes($installedBootstrap)
    $beforeRuntime = [IO.File]::ReadAllBytes($installedRuntime)
    $beforeState = [IO.File]::ReadAllBytes($statePath)
    $env:CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE = '1'
    Invoke-Installer 'update' $common 1 | Out-Null
    Remove-Item Env:CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($installedBootstrap)) -eq [Convert]::ToBase64String($beforeBootstrap)) 'Rollback changed the bootstrap.'
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($installedRuntime)) -eq [Convert]::ToBase64String($beforeRuntime)) 'Rollback changed the runtime.'
    Assert-True ([Convert]::ToBase64String([IO.File]::ReadAllBytes($statePath)) -eq [Convert]::ToBase64String($beforeState)) 'Rollback changed install state.'

    Invoke-Installer 'diagnose' @('-GameDir', $Game) | Out-Null
    Invoke-Installer 'uninstall' @('-GameDir', $Game) | Out-Null
    Assert-True (-not (Test-Path -LiteralPath $pluginDir)) 'Uninstall left Crawl Online files behind.'
    Assert-True ((Get-Content -Raw -LiteralPath (Join-Path $Game 'BepInEx\plugins\OtherMod\keep.txt')) -eq 'keep') 'Uninstall changed another plugin.'
    Assert-True (Test-Path -LiteralPath (Join-Path $Game 'BepInEx\core\BepInEx.dll')) 'Uninstall removed BepInEx.'
    Write-Host 'Windows installer fixture test passed'
} finally {
    Remove-Item Env:CRAWL_ONLINE_TEST_FAIL_AFTER_FIRST_ACTIVATE -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -LiteralPath $Temporary
}
