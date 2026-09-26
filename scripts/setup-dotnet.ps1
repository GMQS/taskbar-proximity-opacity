$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $projectRoot '.dotnet'
$dotnet = Join-Path $installDir 'dotnet.exe'
$version = '10.0.401'

if (-not (Test-Path (Join-Path $installDir "sdk/$version"))) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    $installer = Join-Path $installDir 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Version $version -Architecture x64 -InstallDir $installDir -NoPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$appPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifact/TaskbarProximityOpacity.exe'))
$running = @(Get-Process -Name 'TaskbarProximityOpacity' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and [string]::Equals($_.Path, $appPath, [StringComparison]::OrdinalIgnoreCase) })
$forcedExit = $false
if ($running.Count -gt 0) {
    Write-Host 'Stopping the running TaskbarProximityOpacity...'
    try {
        $exitEvent = [Threading.EventWaitHandle]::OpenExisting('Local\TaskbarProximityOpacity.BuildExit')
        try { [void]$exitEvent.Set() } finally { $exitEvent.Dispose() }
    } catch [Threading.WaitHandleCannotBeOpenedException] {
        # Older builds have no graceful build-exit signal.
    }

    foreach ($process in $running) {
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
            [void]$process.WaitForExit(5000)
            $forcedExit = $true
        }
    }
}

& $dotnet build (Join-Path $projectRoot 'TaskbarProximityOpacity.csproj') -c Release -o (Join-Path $projectRoot 'artifact')
$buildExitCode = $LASTEXITCODE

if ($forcedExit) {
    # A force-closed older build may leave taskbar opacity changed. Run the new
    # build once so its recovery logic restores the taskbar, then exit cleanly.
    $recoveryPath = Join-Path $env:LOCALAPPDATA 'TaskbarProximityOpacity/recovery.json'
    if (Test-Path $recoveryPath) {
        $recoveryProcess = Start-Process -FilePath $appPath -PassThru -WindowStyle Hidden
        if ($buildExitCode -ne 0) {
            Write-Warning 'Build failed. The previous app was restarted to restore the taskbar.'
            exit $buildExitCode
        }
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            Start-Sleep -Milliseconds 250
            try {
                $exitEvent = [Threading.EventWaitHandle]::OpenExisting('Local\TaskbarProximityOpacity.BuildExit')
                try { [void]$exitEvent.Set() } finally { $exitEvent.Dispose() }
                break
            } catch [Threading.WaitHandleCannotBeOpenedException] {
                if ($recoveryProcess.HasExited) { break }
            }
        }
        if (-not $recoveryProcess.WaitForExit(5000)) {
            Write-Warning 'The recovery instance is still running. Exit it from the tray menu.'
        }
    }
}

exit $buildExitCode
