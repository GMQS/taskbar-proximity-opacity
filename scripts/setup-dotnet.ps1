$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $projectRoot '.dotnet'
$dotnet = Join-Path $installDir 'dotnet.exe'
$version = '10.0.401'

if (Test-Path (Join-Path $installDir "sdk/$version")) {
    & $dotnet --version
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$installer = Join-Path $installDir 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Version $version -Architecture x64 -InstallDir $installDir -NoPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet --version
exit $LASTEXITCODE
