$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot '../../src/Takupoke.Win/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Takupoke.Win.exe'
$executable = (Resolve-Path -LiteralPath $executable).Path
$env:TAKUPOKE_OFFLINE_TEST_MODE = '1'
$env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'test-app-data'
dotnet run --project (Join-Path $PSScriptRoot 'Takupoke.Win.UITests.csproj') --configuration Release -- $executable $env:TAKUPOKE_DATA_ROOT
if ($LASTEXITCODE -ne 0) { throw 'Windows UI automation checks failed.' }
