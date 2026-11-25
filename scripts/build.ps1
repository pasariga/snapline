# Builds Snapline into dist\. -Installer also creates both Windows release executables and SHA-256 sums.
param([switch]$Portable, [switch]$Installer, [string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\Snapline\Snapline.csproj'
$dist = Join-Path $root 'dist'

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $proj -Raw)).Project.PropertyGroup.Version |
        Select-Object -First 1
}
if ($Version -cnotmatch '^\d+\.\d+\.\d+(?:-[a-z0-9]+(?:[.-][a-z0-9]+)*)?$') {
    throw "Expected a version such as 1.6.2-windows.1. Got '$Version'."
}

if (-not (Test-Path (Join-Path $root 'src\Snapline\Assets\Snapline.ico'))) {
    & (Join-Path $PSScriptRoot 'make-brand.ps1')
}

if ($Installer) { $Portable = $true }
$outDir = if ($Portable) { Join-Path $dist 'portable' } else { Join-Path $dist 'win-x64' }
$publishArgs = @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '-o', $outDir,
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
    "-p:Version=$Version")
$publishArgs += if ($Portable) { '--self-contained', 'true', '-p:EnableCompressionInSingleFile=true' }
                else { '--self-contained', 'false' }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$builtExe = Join-Path $outDir 'Snapline.exe'
Write-Host "Built $builtExe"

if ($Installer) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        "$([Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 not found.' }

    & $iscc (Join-Path $root 'installer\snapline.iss') "/DVersion=$Version" "/DSource=$outDir" /Q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $setupName = "Snapline-$Version-win-x64-Setup.exe"
    $portableName = "Snapline-$Version-win-x64-Portable.exe"
    $releaseDir = Join-Path $dist "release\$Version"
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $dist $setupName) -Destination (Join-Path $releaseDir $setupName) -Force
    Copy-Item -LiteralPath $builtExe -Destination (Join-Path $releaseDir $portableName) -Force

    $checksums = foreach ($name in $setupName, $portableName) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $releaseDir $name)
        "$($hash.Hash.ToLowerInvariant())  $name"
    }
    Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Value $checksums -Encoding ascii
    Write-Host "Windows release files: $releaseDir"
}
