param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageVersion = & "$PSScriptRoot/store-version.ps1" -Version $Version
if ($Commit -cnotmatch '^[a-f0-9]{40}$') { throw 'A full source commit SHA is required.' }
if (-not $env:TAKUPOKE_CI_TEMP -or -not $env:NUGET_PACKAGES) { throw 'Job-owned temporary and NuGet paths are required.' }
$taskRoot = [IO.Path]::GetFullPath($env:TAKUPOKE_CI_TEMP)
$output = Join-Path $taskRoot 'store-output'
$packages = Join-Path $taskRoot 'store-architectures'
New-Item -ItemType Directory -Path $output, $packages -Force | Out-Null
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object {
    $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe'))
} | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK MakeAppx is required.' }
$makeappx = Join-Path $sdk.FullName 'x64/makeappx.exe'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$studio = & $vswhere -latest -products '*' -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $studio) { throw 'Visual Studio redistributable directory is unavailable.' }
$redist = Get-ChildItem -LiteralPath (Join-Path $studio 'VC/Redist/MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $redist) { throw 'The redistributable VC runtime is unavailable.' }
Add-Type -AssemblyName System.Drawing
foreach ($architecture in @('x64', 'arm64')) {
    $platform = if ($architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
    $payload = Join-Path $taskRoot "store-payload-$architecture"
    dotnet publish src/Takupoke.Win/Takupoke.Win.csproj --configuration Release --runtime "win-$architecture" --self-contained true `
        -p:Platform=$platform -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:Version=$Version -p:FileVersion=$packageVersion -p:InformationalVersion="$Version+$Commit" `
        -p:DebugType=None -p:DebugSymbols=false --output $payload
    if ($LASTEXITCODE -ne 0) { throw "Publishing the $architecture Store payload failed." }
    $crt = Get-ChildItem -LiteralPath (Join-Path $redist.FullName $architecture) -Directory -Filter 'Microsoft.VC*.CRT' |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'vcruntime140.dll') } | Select-Object -First 1
    if (-not $crt) { throw "The VC runtime for $architecture is unavailable." }
    Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' | Copy-Item -Destination $payload
    foreach ($required in @('takupoke.exe', 'App.xbf', 'MainWindow.xbf', 'Microsoft.UI.Xaml.dll', 'coreclr.dll', 'hostfxr.dll', 'vcruntime140.dll', 'msvcp140.dll', 'Legal/privacy.txt', 'Legal/terms.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing $architecture payload file: $required" }
    }
    python scripts/collect-package-notices.py $env:NUGET_PACKAGES $payload licenses/spdx --asset-notices src/Takupoke.Win/Assets/FluentIcons
    if ($LASTEXITCODE -ne 0) { throw 'Dependency notices could not be collected.' }
    Copy-Item -LiteralPath 'licenses/denpa-schedule-csv.txt' -Destination (Join-Path $payload 'Licenses')
    @(
        'Microsoft Visual C++ runtime libraries are redistributed unmodified from the Visual Studio redistributable directory.'
        'Copyright Microsoft Corporation. Microsoft Software License Terms apply.'
        'https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution'
        "Runtime directory version: $($redist.Name)"
    ) | Set-Content -LiteralPath (Join-Path $payload 'Licenses/Microsoft-VC-Runtime.txt') -Encoding utf8
    $image = [Drawing.Image]::FromFile((Resolve-Path 'src/Takupoke.Win/Assets/takupoke.png').Path)
    try {
        foreach ($asset in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
            $bitmap = [Drawing.Bitmap]::new([int]$asset[1], [int]$asset[1])
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($image, 0, 0, $bitmap.Width, $bitmap.Height)
                $bitmap.Save((Join-Path $payload "Assets/$($asset[0]).png"), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $image.Dispose() }
    [xml]$manifest = Get-Content -LiteralPath 'packaging/store/AppxManifest.xml' -Raw
    $manifest.Package.Identity.Version = $packageVersion
    $manifest.Package.Identity.ProcessorArchitecture = $architecture
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))
    foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
        if ($file.Extension -in @('.pdb', '.cs', '.xaml', '.pdf', '.xlsx', '.db', '.sqlite', '.pfx', '.key', '.log') -or
            $file.Name -eq 'AGENTS.md' -or $file.Name.StartsWith('.env') -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'A non-distribution file was found in the Store payload.'
        }
    }
    $packingOutput = & $makeappx pack /d $payload /p (Join-Path $packages "takupoke-$Version-$architecture.msix") /o 2>&1
    if ($LASTEXITCODE -ne 0) { $packingOutput | Write-Output; throw "MSIX schema validation or packing failed for $architecture." }
    Write-Output "MSIX schema validation and packing succeeded for $architecture."
}
$bundleOutput = & $makeappx bundle /d $packages /p (Join-Path $output "takupoke-$Version.msixbundle") /bv $packageVersion /o 2>&1
if ($LASTEXITCODE -ne 0) { $bundleOutput | Write-Output; throw 'MSIX bundle creation failed.' }
Get-ChildItem -LiteralPath $output -Filter '*.msixbundle' | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS-MSIX.txt') -Encoding ascii
Write-Output "Store bundle created: $Version ($packageVersion), source $Commit. Microsoft Store will sign the submitted package."
