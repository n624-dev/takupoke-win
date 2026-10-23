param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Certificate trust and package installation tests are permitted only on an ephemeral GitHub-hosted runner.'
}
& "$PSScriptRoot/store-version.ps1" -Version $Version
if (-not $env:TAKUPOKE_CI_TEMP) { throw 'A job-owned temporary directory is required.' }
Import-Module Appx -UseWindowsPowerShell
$identity = 'n624.484124996A09A'
if (Get-AppxPackage -Name $identity) { throw 'A Store installation already exists; it will not be modified.' }
$bundle = Join-Path $env:TAKUPOKE_CI_TEMP "store-output/takupoke-$Version.msixbundle"
$testBundle = Join-Path $env:TAKUPOKE_CI_TEMP 'signed-installation-test.msixbundle'
Copy-Item -LiteralPath $bundle -Destination $testBundle
$certificate = $null
$trusted = $false
$installed = $false
try {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=680A4AD1-A65A-4F06-A2FA-C6BE82E46F72' `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddDays(1)
    $publicCertificate = Join-Path $env:TAKUPOKE_CI_TEMP 'installation-test.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
    Import-Certificate -FilePath $publicCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    $trusted = $true
    $sdk = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/signtool.exe')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'Windows SDK SignTool is required for installation testing.' }
    & (Join-Path $sdk.FullName 'x64/signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint $testBundle
    if ($LASTEXITCODE -ne 0) { throw 'Signing the temporary installation-test bundle failed.' }
    Add-AppxPackage -Path $testBundle
    $installed = $true
    $package = Get-AppxPackage -Name $identity
    if (-not $package -or $package.Version -ne "$Version.0") { throw 'The installed Store version does not match.' }
    $executable = Join-Path $package.InstallLocation 'takupoke.exe'
    $env:TAKUPOKE_OFFLINE_TEST_MODE = '1'
    $env:TAKUPOKE_TEST_PACKAGED_PROTOCOL = '1'
    $env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'store-installed-ui-data'
    dotnet run --project tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj --configuration Release -- $executable $env:TAKUPOKE_DATA_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'Installed MSIX UI, restart, persistence or protocol checks failed.' }
    $env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'store-installed-screens'
    dotnet run --project tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj --configuration Release --no-build -- --capture $executable $env:TAKUPOKE_DATA_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'Installed MSIX screen checks failed.' }
    powershell.exe -NoProfile -NonInteractive -Command "Set-DisplayResolution -Width 1920 -Height 1080 -Force"
    if ($LASTEXITCODE -ne 0) { throw 'Preparing the ephemeral Store screenshot desktop failed.' }
    $env:TAKUPOKE_DATA_ROOT = Join-Path $env:TAKUPOKE_CI_TEMP 'store-listing-screens'
    dotnet run --project tests/Takupoke.Win.UITests/Takupoke.Win.UITests.csproj --configuration Release --no-build -- --capture-store $executable $env:TAKUPOKE_DATA_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'Native-size Store listing captures failed.' }
} finally {
    if ($installed) {
        $package = Get-AppxPackage -Name $identity
        if ($package) { Remove-AppxPackage -Package $package.PackageFullName }
    }
    if ($certificate) {
        if ($trusted) { Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" -Force }
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force
    }
}
Write-Output 'Installed MSIX UI and synthetic screen checks passed; test installation and temporary certificate trust were removed.'
