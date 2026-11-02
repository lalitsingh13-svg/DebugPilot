[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$SkipTests,
    # Also builds the .vsix with full MSBuild (needs Visual Studio with the "Visual Studio extension development" workload).
    [switch]$BuildVsix
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Always run from the folder containing this script, regardless of the caller's current directory.
Push-Location $PSScriptRoot
try {
    $solution    = Join-Path $PSScriptRoot 'DebugPilot.sln'
    $testProject = Join-Path $PSScriptRoot 'tests/DebugPilot.Core.Tests/DebugPilot.Core.Tests.csproj'

    foreach ($path in @($solution, $testProject)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file not found: $path" }
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK was not found. Install the .NET 8 SDK, reopen PowerShell, and rerun .\setup.ps1.'
    }

    # NOTE: "dotnet build DebugPilot.sln" cannot build DebugPilot.Package (a legacy VSIX project that needs full MSBuild).
    # Core + tests are built through the test project; the VSIX is built separately with -BuildVsix.
    if (-not $SkipRestore) {
        Write-Host '==> Restoring Core and tests...' -ForegroundColor Cyan
        & dotnet restore $testProject
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    }

    Write-Host '==> Building Core and tests (Release)...' -ForegroundColor Cyan
    & dotnet build $testProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    if (-not $SkipTests) {
        Write-Host '==> Running tests...' -ForegroundColor Cyan
        & dotnet test $testProject --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
    }

    if ($BuildVsix) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe not found. Install Visual Studio (with the extension development workload).' }
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not $msbuild) { throw 'MSBuild.exe was not found by vswhere.' }

        Write-Host "==> Building the VSIX with $msbuild ..." -ForegroundColor Cyan
        & $msbuild $solution /restore /t:Build /p:Configuration=Release /p:Platform='Any CPU' /m
        if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

        $vsix = Get-ChildItem $PSScriptRoot -Filter '*.vsix' -Recurse | Where-Object { $_.FullName -match '\\bin\\Release\\' }
        if (-not $vsix) { throw 'Build finished but no .vsix was produced. Check the build output for VSCT/VSSDK errors.' }
        $vsix | ForEach-Object { Write-Host ("VSIX: " + $_.FullName + " (" + $_.Length + " bytes)") -ForegroundColor Green }
    }

    Write-Host 'DebugPilot checks completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
