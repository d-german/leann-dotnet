#!/usr/bin/env pwsh
# publish-all.ps1 — Build leann-dotnet for Windows and macOS
param(
    [string]$Configuration = "Release",
    [switch]$DeployToSharedDrive
)

$projectDir = "$PSScriptRoot\src\LeannMcp"
$publishRoot = "$PSScriptRoot\publish"

$targets = @(
    @{ RID = "win-x64";    OutputDir = "$publishRoot\win-x64";    ExeName = "leann-dotnet.exe" },
    @{ RID = "osx-arm64";  OutputDir = "$publishRoot\osx-arm64";  ExeName = "leann-dotnet" }
)

foreach ($target in $targets) {
    Write-Host "`n=== Publishing $($target.RID) ===" -ForegroundColor Cyan
    dotnet publish $projectDir -c $Configuration -r $target.RID -o $target.OutputDir 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to publish $($target.RID)"
        exit 1
    }
    $exe = Join-Path $target.OutputDir $target.ExeName
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  -> $exe ($size MB)" -ForegroundColor Green
}

if ($DeployToSharedDrive) {
    Write-Host "`n=== Deploying to shared drive ===" -ForegroundColor Cyan
    $sharedBin = "Z:\.leann\bin"
    if (!(Test-Path $sharedBin)) { New-Item -ItemType Directory -Path $sharedBin -Force | Out-Null }

    Copy-Item "$publishRoot\win-x64\leann-dotnet.exe" "$sharedBin\leann-dotnet.exe" -Force
    Copy-Item "$publishRoot\osx-arm64\leann-dotnet" "$sharedBin\leann-dotnet-mac" -Force
    Write-Host "  -> Deployed to $sharedBin" -ForegroundColor Green
}

Write-Host "`nDone!" -ForegroundColor Green