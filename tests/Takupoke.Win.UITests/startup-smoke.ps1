$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot '../../src/Takupoke.Win/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/takupoke.exe'
$executable = (Resolve-Path -LiteralPath $executable).Path
$env:TAKUPOKE_OFFLINE_TEST_MODE = '1'
$env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'test-app-data'
$testStarted = Get-Date
dotnet run --project (Join-Path $PSScriptRoot 'Takupoke.Win.UITests.csproj') --configuration Release -- $executable $env:TAKUPOKE_DATA_ROOT
$navigationPassed = $LASTEXITCODE -eq 0
if (!$navigationPassed) {
    # Only this app runs with an isolated synthetic offline data root.
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $testStarted } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in @('.NET Runtime', 'Application Error') -and $_.Message.Contains('Takupoke.Win') } |
        Select-Object -First 4 -ExpandProperty Message | Write-Output
    Write-Output 'Windows UI automation failed; collecting isolated synthetic screens before reporting the failure.'
}
$env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'synthetic-screen-review'
dotnet run --project (Join-Path $PSScriptRoot 'Takupoke.Win.UITests.csproj') --configuration Release --no-build -- --capture $executable $env:TAKUPOKE_DATA_ROOT
if ($LASTEXITCODE -ne 0) { throw 'Isolated synthetic screen review failed.' }

if (!$navigationPassed) { throw 'Windows UI automation checks failed.' }
