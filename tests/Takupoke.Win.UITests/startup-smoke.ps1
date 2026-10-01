$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot '../../src/Takupoke.Win/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Takupoke.Win.exe'
$executable = (Resolve-Path -LiteralPath $executable).Path
$process = Start-Process -FilePath $executable -PassThru

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $process.Refresh()
        if ($process.HasExited) {
            throw "App exited before opening its development window (exit code $($process.ExitCode))."
        }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -eq 'たくポケ Win') {
            Write-Output 'WinUI development window opened successfully on the CI runner.'
            exit 0
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'The development window did not open before the timeout.'
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000) | Out-Null
    }
    $process.Dispose()
}
