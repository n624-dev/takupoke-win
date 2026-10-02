param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRoot = $env:TAKUPOKE_CI_TEMP
if (-not $taskRoot -or $Version -notmatch '^\d+\.\d+\.\d+-dev\.\d+$') { throw 'Invalid test parameters.' }
$setup = Join-Path $taskRoot "release-assets/TakupokeWin-$Version-win-x64-Setup.exe"
$installDir = Join-Path $taskRoot 'installed-app with spaces'
$exe = Join-Path $installDir 'Takupoke.Win.exe'
$env:TAKUPOKE_OFFLINE_TEST_MODE = '1'
$env:TAKUPOKE_DATA_ROOT = Join-Path $taskRoot 'installed-app-test-data'
# The app must use its included .NET runtime rather than an installed development runtime.
$env:DOTNET_ROOT = Join-Path $taskRoot 'not-an-installed-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$testStarted = Get-Date
$env:DOTNET_HOST_TRACE = '1'
$env:DOTNET_HOST_TRACEFILE = Join-Path $taskRoot 'installed-app-host.log'
function Run-Installer([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -PassThru -Wait
    try { if ($process.ExitCode -ne 0) { throw "Installer operation failed: $($process.ExitCode)" } }
    finally { $process.Dispose() }
}
function Run-UiChecks {
    dotnet build tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'UI test helper did not build.' }
    dotnet exec tests/Takupoke.Win.UITests/bin/Release/net10.0-windows/Takupoke.Win.UITests.dll $exe $env:TAKUPOKE_DATA_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'Installed app UI tests failed.' }
}
try {
    Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot 'setup-first.log')`"")
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Installer did not create the app executable.' }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'たくポケ Win.lnk'
    if (-not (Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut was not created.' }
    $command = (Get-ItemProperty -LiteralPath 'HKCU:/Software/Classes/jp.n624.takupoke.win/shell/open/command').'(default)'
    if ($command -notlike "*$exe*") { throw 'Protocol callback does not point to the installed app.' }
    Run-UiChecks
    $preferences = Join-Path $env:TAKUPOKE_DATA_ROOT 'preferences.json'
    $before = (Get-FileHash -LiteralPath $preferences).Hash
    Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot 'setup-update.log')`"")
    if ((Get-FileHash -LiteralPath $preferences).Hash -ne $before) { throw 'Reinstallation changed personal settings.' }
    Run-UiChecks
    Run-Installer (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (Test-Path -LiteralPath $exe) { throw 'App executable remains after uninstall.' }
    if (Test-Path -LiteralPath $shortcut) { throw 'App shortcut remains after uninstall.' }
    if (Test-Path -LiteralPath 'HKCU:/Software/Classes/jp.n624.takupoke.win') { throw 'Protocol registration remains after uninstall.' }
    if (-not (Test-Path -LiteralPath $preferences)) { throw 'Uninstall unexpectedly deleted personal settings.' }
    Write-Output 'Verified installer deployment, published app UI, reinstallation with preserved settings, and uninstall cleanup.'
} catch {
    # This job uses exclusively synthetic data. Capture runtime diagnostics only
    # for this app, never machine-wide event logs or school files.
    if (Test-Path -LiteralPath $env:DOTNET_HOST_TRACEFILE) {
        Get-Content -LiteralPath $env:DOTNET_HOST_TRACEFILE -Tail 35 | Write-Output
    }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $testStarted } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('.NET Runtime', 'Application Error') -and $_.Message.Contains('Takupoke.Win') } |
        Select-Object -First 4 -ExpandProperty Message | Write-Output
    throw
} finally {
    if (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe')) {
        Run-Installer (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
    Remove-Item Env:/DOTNET_ROOT, Env:/DOTNET_ROOT_X64, Env:/DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
    Remove-Item Env:/DOTNET_HOST_TRACE, Env:/DOTNET_HOST_TRACEFILE -ErrorAction SilentlyContinue
}
