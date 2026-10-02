param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)-dev\.(\d+)$') { throw 'Only a development version such as 0.1.0-dev.1 is permitted.' }
$fileVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).$($Matches[4])"
if ($Commit -notmatch '^[a-f0-9]{40}$') { throw 'A full commit SHA is required.' }
if (-not $env:TAKUPOKE_CI_TEMP) { throw 'Job-owned temporary root is required.' }
$taskRoot = $env:TAKUPOKE_CI_TEMP
$output = Join-Path $taskRoot 'release-assets'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The runner does not have Inno Setup 6.' }
Write-Output "Installer compiler: $((Get-Item $compiler).VersionInfo.FileVersion)"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$studio = & $vswhere -latest -products '*' -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $studio) { throw 'Visual Studio redist directory is unavailable.' }
$redistRoot = Join-Path $studio 'VC/Redist/MSVC'
$redist = Get-ChildItem -LiteralPath $redistRoot -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $redist) { throw 'No redistributable native runtime was found.' }
$bootstrapper = Join-Path $taskRoot 'MicrosoftEdgeWebview2Setup.exe'
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -TimeoutSec 90
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') { throw 'WebView2 bootstrapper signature could not be verified.' }

function Get-PeMachine([string]$Path) {
    $inputStream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($inputStream)
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Missing PE header.' }
        $inputStream.Position = 0x3c
        $offset = $reader.ReadInt32(); $inputStream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw 'Invalid PE signature.' }
        return $reader.ReadUInt16()
    } finally { $inputStream.Dispose() }
}

foreach ($arch in @('x64', 'arm64')) {
    $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $payload = Join-Path $taskRoot "publish-$arch"
    dotnet publish src/Takupoke.Win/Takupoke.Win.csproj --configuration Release --runtime "win-$arch" --self-contained true `
        -p:Platform=$platform -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:Version=$Version -p:FileVersion=$fileVersion -p:InformationalVersion="$Version+$Commit" -p:DebugType=None -p:DebugSymbols=false --output $payload
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $arch." }
    $crt = Get-ChildItem -LiteralPath (Join-Path $redist.FullName $arch) -Directory -Filter 'Microsoft.VC*.CRT' |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'vcruntime140.dll') } | Select-Object -First 1
    if (-not $crt) { throw "No redistributable CRT for $arch in runtime version $($redist.Name)." }
    Write-Output "Native runtime: $($redist.Name) / $arch / $($crt.Name)"
    Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' | Copy-Item -Destination $payload -Force
    foreach ($required in @('Takupoke.Win.exe', 'App.xbf', 'MainWindow.xbf', 'Legal/terms.txt', 'Legal/privacy.txt', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.UI.Xaml.dll', 'vcruntime140.dll', 'msvcp140.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Published $arch payload is missing $required." }
    }
    $expectedMachine = if ($arch -eq 'x64') { 0x8664 } else { 0xaa64 }
    foreach ($binary in @('Takupoke.Win.exe', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
        if ((Get-PeMachine (Join-Path $payload $binary)) -ne $expectedMachine) { throw "Wrong architecture in $binary." }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $payload 'Takupoke.Win.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'The published app is not .NET self-contained.' }
    Copy-Item -LiteralPath 'packaging/development-info.txt' -Destination (Join-Path $payload 'インストールと注意事項.txt')
    python scripts/collect-package-notices.py $env:NUGET_PACKAGES $payload licenses/spdx
    if ($LASTEXITCODE -ne 0) { throw 'Dependency notices could not be collected.' }
    $licenseOutput = Join-Path $payload 'Licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Copy-Item -LiteralPath 'licenses/denpa-schedule-csv.txt' -Destination $licenseOutput
    Copy-Item -LiteralPath (Join-Path (Split-Path $compiler) 'license.txt') -Destination (Join-Path $licenseOutput 'InnoSetup.txt')
    @(
        'Microsoft Visual C++ runtime libraries are redistributed unmodified from the Visual Studio redistributable directory.'
        'Copyright Microsoft Corporation. Microsoft Software License Terms apply.'
        'https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution'
        "Runtime directory version: $($redist.Name)"
    ) | Set-Content -LiteralPath (Join-Path $licenseOutput 'Microsoft-VC-Runtime.txt') -Encoding utf8
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    foreach ($file in $files) {
        if ($file.Extension -in @('.pdb', '.cs', '.xaml', '.pdf', '.xlsx', '.sqlite', '.db', '.pfx', '.key', '.log') `
            -or $file.Name -eq 'AGENTS.md' -or $file.Name.StartsWith('.env')) { throw 'A non-distribution file was found in the published payload.' }
    }
    @{
        version = $Version; commit = $Commit; architecture = $arch; development = $true; signed = $false
        files = @($files | Sort-Object FullName | ForEach-Object { @{ path = [IO.Path]::GetRelativePath($payload, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Encoding utf8
    & $compiler "/DAppVersion=$Version" "/DTargetArch=$arch" "/DPublishDir=$payload" "/DOutputDir=$output" "/DWebViewBootstrapper=$bootstrapper" packaging/takupoke-win.iss
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $arch." }
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath (Join-Path $output "TakupokeWin-$Version-win-$arch.zip") -CompressionLevel Optimal
    Write-Output "Prepared $arch self-contained payload and installer."
}
Copy-Item -LiteralPath 'packaging/development-info.txt' -Destination (Join-Path $output 'INSTALL.txt')
Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
