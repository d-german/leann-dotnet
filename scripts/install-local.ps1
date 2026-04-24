#requires -Version 7
<#
.SYNOPSIS
    Pack and install LeannMcp as a global dotnet tool from a local source.
.PARAMETER Version
    Optional explicit version. If omitted, parsed from Directory.Build.props.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$projPath   = Join-Path $repoRoot 'src/LeannMcp/LeannMcp.csproj'
$propsPath  = Join-Path $repoRoot 'Directory.Build.props'
$packageDir = Join-Path $repoRoot 'src/LeannMcp/bin/Release'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $xml = [xml](Get-Content -LiteralPath $propsPath -Raw)
    $Version = $xml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Could not parse <Version> from $propsPath"
    }
}

Write-Host "==> Packing LeannMcp $Version" -ForegroundColor Cyan
dotnet pack $projPath -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed (exit $LASTEXITCODE)" }

Write-Host "==> Uninstalling existing global tool (if any)" -ForegroundColor Cyan
dotnet tool uninstall -g LeannMcp 2>$null | Out-Null

Write-Host "==> Installing LeannMcp $Version from $packageDir" -ForegroundColor Cyan
dotnet tool install -g LeannMcp --add-source $packageDir --version $Version
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed (exit $LASTEXITCODE)" }

Write-Host "==> Installed. Verifying entry point:" -ForegroundColor Green
leann-mcp --help | Select-Object -First 2
