param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('x64', 'arm64')][string[]]$Architectures = @('x64', 'arm64')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageVersion = & "$PSScriptRoot/store-version.ps1" -Version $Version
if (-not $env:TAKUPOKE_CI_TEMP) { throw 'A job-owned temporary directory is required.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($architecture in $Architectures) {
    $package = Join-Path $env:TAKUPOKE_CI_TEMP "store-architectures/takupoke-$Version-$architecture.msix"
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @{}
        foreach ($item in $archive.Entries) {
            if ($entries.ContainsKey($item.FullName)) { throw 'Ambiguous package paths differ only in case.' }
            $entries[$item.FullName] = $item
        }
        $entry = $entries['AppxManifest.xml']
        if (-not $entry) { throw 'The MSIX has no manifest.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($manifest.Package.Identity.Name -cne 'n624.484124996A09A' -or
            $manifest.Package.Identity.Publisher -cne 'CN=680A4AD1-A65A-4F06-A2FA-C6BE82E46F72' -or
            $manifest.Package.Identity.Version -cne $packageVersion -or
            $manifest.Package.Identity.ProcessorArchitecture -cne $architecture) { throw 'The Store package identity does not match.' }
        foreach ($binary in @('takupoke.exe', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
            $binaryEntry = $entries[$binary]
            if (-not $binaryEntry) { throw "Missing binary: $binary" }
            $memory = [IO.MemoryStream]::new()
            try {
                $stream = $binaryEntry.Open()
                try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
                $memory.Position = 0
                $reader = [IO.BinaryReader]::new($memory)
                if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Missing DOS header.' }
                $memory.Position = 0x3c
                $offset = $reader.ReadInt32()
                $memory.Position = $offset
                if ($reader.ReadUInt32() -ne 0x4550) { throw 'Missing PE header.' }
                $expected = if ($architecture -eq 'x64') { 0x8664 } else { 0xaa64 }
                if ($reader.ReadUInt16() -ne $expected) { throw "Wrong architecture in $binary." }
            } finally { $memory.Dispose() }
        }
        foreach ($required in @('App.xbf', 'MainWindow.xbf', 'THIRD-PARTY-NOTICES.txt', 'Legal/privacy.txt', 'Legal/terms.txt', 'Assets/StoreLogo.png', 'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
            if (-not $entries.ContainsKey($required)) { throw "The Store package is missing $required." }
        }
        $runtimeEntry = $entries['takupoke.runtimeconfig.json']
        if (-not $runtimeEntry) { throw 'The runtime configuration is missing.' }
        $reader = [IO.StreamReader]::new($runtimeEntry.Open())
        try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if (-not $runtime.runtimeOptions.PSObject.Properties['includedFrameworks'] -or
            -not $runtime.runtimeOptions.includedFrameworks) { throw 'The Store app is not .NET self-contained.' }
    } finally { $archive.Dispose() }
}
Write-Output "Verified MSIX architectures $($Architectures -join ', '), Store identities, compiled UI, runtime binaries, notices, and visual assets."
