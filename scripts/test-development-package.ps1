param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRoot = $env:TAKUPOKE_CI_TEMP
if (-not $taskRoot -or $Version -notmatch '^\d+\.\d+\.\d+-dev\.\d+$') { throw 'Invalid test parameters.' }
$setup = Join-Path $taskRoot "release-assets/takupoke-$Version-x64-Setup.exe"
$installDir = Join-Path $taskRoot 'installed-app with spaces'
$exe = Join-Path $installDir 'takupoke.exe'
$previousInstallDir = $null
$ownedInstallDirs = [System.Collections.Generic.List[string]]::new()
$ownedInstallDirs.Add($installDir)
$ownedStartupCommands = [System.Collections.Generic.List[string]]::new()
$runKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'たくポケ.lnk'
$legacyShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'たくポケ Win.lnk'
$legacyShortcutOwned = $false
$uninstallKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{00C9D0A0-362C-4F7A-93E7-C25D5160C279}_is1'
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
function Get-StartupValue([string]$Name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    if ($null -eq $key) { return $null }
    try { return $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
    finally { $key.Dispose() }
}
function Get-LegacyInstaller {
    # Exercise the actual released installer, rather than relabeling a new app.
    $directory = Join-Path $taskRoot 'legacy-dev5-release'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $name = 'TakupokeWin-0.1.0-dev.5-win-x64-Setup.exe'
    $base = 'https://github.com/n624-dev/takupoke-win/releases/download/v0.1.0-dev.5'
    $checksums = Join-Path $directory 'SHA256SUMS.txt'
    $installer = Join-Path $directory $name
    Invoke-WebRequest -Uri "$base/SHA256SUMS.txt" -OutFile $checksums
    $checksumEntries = @(Get-Content -LiteralPath $checksums | Where-Object { $_ -cmatch ('^[0-9a-f]{64}  ' + [regex]::Escape($name) + '$') })
    if ($checksumEntries.Count -ne 1) { throw 'The historical installer checksum is missing or ambiguous.' }
    $hash = $checksumEntries[0].Substring(0, 64)
    # Pin the original release checksum as well as checking its published file.
    if ($hash -cne '70e3c8be8a18c04ec40d88f1753e7727fcf2d594d638436f10bc210a295c2d03') { throw 'The historical release checksum changed.' }
    Invoke-WebRequest -Uri "$base/$name" -OutFile $installer
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw 'The historical installer failed checksum verification.' }
    return $installer
}
function Same-FilePath([string]$Actual, [string]$Expected) {
    if ([string]::IsNullOrWhiteSpace($Actual)) { return $false }
    return [string]::Equals([IO.Path]::GetFullPath($Actual.Trim('"')).TrimEnd([char]92),
        [IO.Path]::GetFullPath($Expected).TrimEnd([char]92), [StringComparison]::OrdinalIgnoreCase)
}
function Check-InstalledBranding {
    if (-not (Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut was not created.' }
    if (Test-Path -LiteralPath $legacyShortcut) { throw 'The previous product-name shortcut remains.' }
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.DisplayName -ne 'たくポケ') { throw 'Installed app display name is incorrect.' }
    if ($registration.InstallLocation.TrimEnd([char]92) -ne $installDir.TrimEnd([char]92)) { throw 'The installer did not use the selected app directory.' }
    if ((Get-Item -LiteralPath $exe).VersionInfo.ProductName -ne 'たくポケ') { throw 'The executable product name is incorrect.' }
    $description = (Get-ItemProperty -LiteralPath 'HKCU:/Software/Classes/jp.n624.takupoke.win').'(default)'
    if ($description -ne 'たくポケ') { throw 'Protocol display name is incorrect.' }
    $command = (Get-ItemProperty -LiteralPath 'HKCU:/Software/Classes/jp.n624.takupoke.win/shell/open/command').'(default)'
    if ($command -ne "`"$exe`" `"----ms-protocol:%1`"") { throw 'Protocol callback command is incompatible with App SDK activation.' }
    $icon = Join-Path $installDir 'Assets/takupoke.ico'
    if (-not (Test-Path -LiteralPath $icon)) { throw 'The published app icon is missing.' }
    $iconLicense = Join-Path $installDir 'Licenses/FluentSystemIcons.txt'
    if (-not (Test-Path -LiteralPath $iconLicense)) { throw 'The original icon license is missing.' }
    if ((Get-FileHash -LiteralPath $iconLicense).Hash -ne (Get-FileHash -LiteralPath 'src/Takupoke.Win/Assets/FluentIcons/LICENSE.txt').Hash) { throw 'The published icon license was changed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installDir 'Licenses/FluentSystemIcons-source.json'))) { throw 'The pinned icon source provenance is missing.' }
    $shell = New-Object -ComObject WScript.Shell
    $entry = $null
    try {
        $entry = $shell.CreateShortcut($shortcut)
        if (!(Same-FilePath $entry.TargetPath $exe) -or !(Same-FilePath $entry.WorkingDirectory $installDir)) {
            throw "The start menu entry points to another installation. Target=[$($entry.TargetPath)] expected=[$exe]; working=[$($entry.WorkingDirectory)] expected=[$installDir]."
        }
        if ($entry.IconLocation -ne "$icon,0") { throw 'The start menu entry does not use the app icon.' }
    } finally {
        if ($null -ne $entry) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($entry) | Out-Null }
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }
}
function Run-UiChecks {
    dotnet build tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'UI test helper did not build.' }
    dotnet exec tests/Takupoke.Win.UITests/bin/Release/net10.0-windows/Takupoke.Win.UITests.dll $exe $env:TAKUPOKE_DATA_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'Installed app UI tests failed.' }
}
try {
    if ($null -ne (Get-StartupValue 'takupoke') -or $null -ne (Get-StartupValue 'TakupokeWin')) { throw 'Refusing to modify pre-existing startup registration in this test runner.' }
    Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot 'setup-first.log')`"")
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Installer did not create the app executable.' }
    Check-InstalledBranding
    Run-UiChecks
    $installedVersion = (Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion
    dotnet exec tests/Takupoke.Win.UITests/bin/Release/net10.0-windows/Takupoke.Win.UITests.dll --check-installer $setup '再インストール'
    if ($LASTEXITCODE -ne 0) { throw 'Reinstall wizard display was not verified.' }
    try {
        # Simulate an older installed version only in this runner-owned installation.
        Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value '0.0.0-dev.0'
        dotnet exec tests/Takupoke.Win.UITests/bin/Release/net10.0-windows/Takupoke.Win.UITests.dll --check-installer $setup '更新'
        if ($LASTEXITCODE -ne 0) { throw 'Upgrade wizard display was not verified.' }
    } finally { Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $installedVersion }
    $preferences = Join-Path $env:TAKUPOKE_DATA_ROOT 'preferences.json'
    $savedPreferences = Get-Content -LiteralPath $preferences -Raw | ConvertFrom-Json
    $savedPreferences | Add-Member -NotePropertyName autoStart -NotePropertyValue $true -Force
    $savedPreferences | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $preferences -Encoding utf8
    $currentStartup = "`"$exe`" --background"
    $ownedStartupCommands.Add($currentStartup)
    if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
    New-ItemProperty -LiteralPath $runKey -Name takupoke -Value $currentStartup -PropertyType String -Force | Out-Null
    $before = (Get-FileHash -LiteralPath $preferences).Hash
    # Exercise a registered installation moving to the new folder. The old
    # uninstaller owns its payload, but must leave unrelated files and app data.
    $previousInstallDir = $installDir
    $unrelated = Join-Path $previousInstallDir 'unrelated-file.txt'
    Set-Content -LiteralPath $unrelated -Value 'Fictional file outside the installer payload.' -Encoding utf8
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
    Copy-Item -LiteralPath $shortcut -Destination $legacyShortcut
    $legacyShortcutOwned = $true
    $installDir = Join-Path $taskRoot 'takupoke installed with spaces'
    $exe = Join-Path $installDir 'takupoke.exe'
    $ownedInstallDirs.Add($installDir)
    Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot 'setup-move.log')`"")
    $currentStartup = "`"$exe`" --background"
    $ownedStartupCommands.Add($currentStartup)
    if ((Get-StartupValue 'takupoke') -cne $currentStartup) { throw 'Silent folder migration lost startup before the app was launched.' }
    if (Test-Path -LiteralPath (Join-Path $previousInstallDir 'takupoke.exe')) { throw 'The previous executable remains after moving the installation.' }
    if (-not (Test-Path -LiteralPath $unrelated)) { throw 'Moving the installation deleted an unrelated file.' }
    if ((Get-FileHash -LiteralPath $unrelated).Hash -ne $unrelatedHash) { throw 'Moving the installation changed an unrelated file.' }
    Check-InstalledBranding
    if ((Get-FileHash -LiteralPath $preferences).Hash -ne $before) { throw 'Moving the installation changed personal settings.' }
    Run-UiChecks
    $before = (Get-FileHash -LiteralPath $preferences).Hash
    Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot 'setup-update.log')`"")
    if ((Get-StartupValue 'takupoke') -cne $currentStartup) { throw 'Same-folder reinstall lost startup before the app was launched.' }
    if ((Get-FileHash -LiteralPath $preferences).Hash -ne $before) { throw 'Reinstallation changed personal settings.' }
    Check-InstalledBranding
    Run-UiChecks
    Run-Installer (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if ($null -ne (Get-StartupValue 'takupoke')) { throw 'App startup registration remains after uninstall.' }
    if (Test-Path -LiteralPath $exe) { throw 'App executable remains after uninstall.' }
    if (Test-Path -LiteralPath $shortcut) { throw 'App shortcut remains after uninstall.' }
    if (Test-Path -LiteralPath 'HKCU:/Software/Classes/jp.n624.takupoke.win') { throw 'Protocol registration remains after uninstall.' }
    if (-not (Test-Path -LiteralPath $preferences)) { throw 'Uninstall unexpectedly deleted personal settings.' }
    $legacySetup = Get-LegacyInstaller
    # Synthetic saved settings model an existing user with startup enabled.
    $savedPreferences = Get-Content -LiteralPath $preferences -Raw | ConvertFrom-Json
    $savedPreferences | Add-Member -NotePropertyName autoStart -NotePropertyValue $true -Force
    $savedPreferences | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $preferences -Encoding utf8
    $before = (Get-FileHash -LiteralPath $preferences).Hash
    foreach ($scenario in @('same-folder', 'different-folder', 'unrelated-startup')) {
        $legacyDir = Join-Path $taskRoot "legacy dev5 $scenario with spaces"
        $ownedInstallDirs.Add($legacyDir)
        # /VERYSILENT skips the historical setup's post-install app launch.
        Run-Installer $legacySetup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$legacyDir`"", "/LOG=`"$(Join-Path $taskRoot "setup-dev5-$scenario.log")`"")
        $legacyExe = Join-Path $legacyDir 'Takupoke.Win.exe'
        if (-not (Test-Path -LiteralPath $legacyExe)) { throw 'The historical installer did not install its original executable.' }
        $legacyPayloadNames = @(Get-ChildItem -LiteralPath $legacyDir -File | Where-Object { $_.Name.StartsWith('Takupoke.Win.', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -ExpandProperty Name)
        if ($legacyPayloadNames.Count -lt 5) { throw 'The historical executable, DLL, and runtime metadata were not installed.' }
        if (-not (Test-Path -LiteralPath $legacyShortcut)) { throw 'The historical installer did not create its original shortcut.' }
        $legacyShortcutOwned = $true
        $unrelated = Join-Path $legacyDir 'unrelated-file.txt'
        Set-Content -LiteralPath $unrelated -Value "Synthetic unrelated file for $scenario." -Encoding utf8
        $unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
        $legacyCommand = "`"$legacyExe`" --background"
        $ownedStartupCommands.Add($legacyCommand)
        if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
        New-ItemProperty -LiteralPath $runKey -Name TakupokeWin -Value $legacyCommand -PropertyType String -Force | Out-Null
        $installDir = if ($scenario -eq 'same-folder') { $legacyDir } else { Join-Path $taskRoot "takupoke upgraded $scenario with spaces" }
        $ownedInstallDirs.Add($installDir)
        $exe = Join-Path $installDir 'takupoke.exe'
        $newCommand = "`"$exe`" --background"
        $ownedStartupCommands.Add($newCommand)
        $unrelatedStartup = "`"$(Join-Path $taskRoot 'fictional unrelated app.exe')`" --fixture"
        if ($scenario -eq 'unrelated-startup') {
            $ownedStartupCommands.Add($unrelatedStartup)
            New-ItemProperty -LiteralPath $runKey -Name takupoke -Value $unrelatedStartup -PropertyType String -Force | Out-Null
        }
        Run-Installer $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $taskRoot "setup-legacy-upgrade-$scenario.log")`"")
        # Check immediately, before launching the new app can repair startup.
        $expectedStartup = if ($scenario -eq 'unrelated-startup') { $unrelatedStartup } else { $newCommand }
        if ((Get-StartupValue 'takupoke') -cne $expectedStartup) { throw "Silent $scenario upgrade lost startup or overwrote an unrelated command." }
        if ($null -ne (Get-StartupValue 'TakupokeWin')) { throw 'The old startup registration remains after upgrade.' }
        foreach ($name in $legacyPayloadNames) {
            if (Test-Path -LiteralPath (Join-Path $legacyDir $name)) { throw "Legacy app payload remains after $scenario upgrade: $name" }
        }
        if (-not (Test-Path -LiteralPath $unrelated) -or (Get-FileHash -LiteralPath $unrelated).Hash -ne $unrelatedHash) { throw 'Legacy upgrade changed an unrelated file.' }
        if ((Get-FileHash -LiteralPath $preferences).Hash -ne $before) { throw 'Legacy upgrade changed saved settings.' }
        Check-InstalledBranding
        Run-UiChecks
        $before = (Get-FileHash -LiteralPath $preferences).Hash
        Run-Installer (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        if ($scenario -eq 'unrelated-startup') {
            if ((Get-StartupValue 'takupoke') -cne $unrelatedStartup) { throw 'Uninstall removed an unrelated startup command.' }
            Remove-ItemProperty -LiteralPath $runKey -Name takupoke
        } elseif ($null -ne (Get-StartupValue 'takupoke')) { throw 'Uninstall left the migrated startup registration.' }
        if ((Get-FileHash -LiteralPath $preferences).Hash -ne $before) { throw 'Uninstall changed saved settings.' }
    }
    Write-Output 'Verified installed UI and branding, new/same-folder reinstall, actual dev.5 upgrades in both folders, silent startup migration, unrelated file/startup and settings preservation, and uninstall cleanup.'
} catch {
    # This job uses exclusively synthetic data. Capture runtime diagnostics only
    # for this app, never machine-wide event logs or school files.
    if (Test-Path -LiteralPath $env:DOTNET_HOST_TRACEFILE) {
        Get-Content -LiteralPath $env:DOTNET_HOST_TRACEFILE -Tail 35 | Write-Output
    }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $testStarted } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('.NET Runtime', 'Application Error') -and ($_.Message.Contains('takupoke.exe') -or $_.Message.Contains('Takupoke.Win')) } |
        Select-Object -First 4 -ExpandProperty Message | Write-Output
    throw
} finally {
    foreach ($directory in ($ownedInstallDirs | Select-Object -Unique)) {
        if ($directory -and (Test-Path -LiteralPath (Join-Path $directory 'unins000.exe'))) {
            Run-Installer (Join-Path $directory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        }
    }
    foreach ($name in @('takupoke', 'TakupokeWin')) {
        $command = Get-StartupValue $name
        if ($null -ne $command -and $ownedStartupCommands.Contains([string]$command)) { Remove-ItemProperty -LiteralPath $runKey -Name $name }
    }
    if ($legacyShortcutOwned -and (Test-Path -LiteralPath $legacyShortcut)) { Remove-Item -LiteralPath $legacyShortcut }
    Remove-Item Env:/DOTNET_ROOT, Env:/DOTNET_ROOT_X64, Env:/DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
    Remove-Item Env:/DOTNET_HOST_TRACE, Env:/DOTNET_HOST_TRACEFILE -ErrorAction SilentlyContinue
}
