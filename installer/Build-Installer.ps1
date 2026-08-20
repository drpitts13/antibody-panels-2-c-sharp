#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir
$projectPath = Join-Path $repoRoot "AntibodyPanels\AntibodyPanels.csproj"
$issPath = Join-Path $installerDir "AntibodyPanels.iss"
$publishDir = Join-Path $repoRoot "AntibodyPanels\bin\Release\net8.0-windows\win-x64\publish"
$outputDir = Join-Path $installerDir "output"

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Error @"
Inno Setup 6 was not found. Install it, then re-run this script.

Download: https://jrsoftware.org/isdl.php
Expected location: C:\Program Files (x86)\Inno Setup 6\ISCC.exe
"@
}

Write-Host "Publishing Antibody Panels (win-x64, self-contained)..."
dotnet publish $projectPath `
    -c Release `
    -p:PublishProfile=Win64Installer `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir "AntibodyPanels.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish succeeded but AntibodyPanels.exe was not found at $publishDir."
}

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Write-Host "Compiling installer with Inno Setup..."
& $iscc $issPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$setupExe = Join-Path $outputDir "AntibodyPanels-Setup-2.0.exe"
if (-not (Test-Path $setupExe)) {
    throw "Installer was not created at $setupExe."
}

Write-Host "Installer created: $setupExe"
