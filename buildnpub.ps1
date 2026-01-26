# BUILD AND PUBLISH SCRIPT FOR .NET PROJECTS
# - to pack all packages:
#   .\buildnpub.ps1 -Pack
# - to pack and push locally:
#   .\buildnpub.ps1 -Pack -PushLocal
# - to pack and push to NuGet.org:
#   .\buildnpub.ps1 -Pack -PushNuGet

param(
    [switch]$Pack,
    [switch]$PushLocal,
    [switch]$PushNuGet,
    [string]$LocalFeed = "C:\Projects\_NuGet",
    [string]$NuGetExe = "C:\Exe\nuget.exe"
)

Write-Host "Build & Publish Script" -ForegroundColor Cyan

#region CONFIGURATION - Customize this section for each solution
# ============================================================
# Define projects in dependency order (no dependencies first, then dependents).
# Order determined by analyzing PackageReference elements in Release configuration.
# Projects with no internal dependencies can be in any order relative to each other.
$projectOrder = @(
    # Layer 0: no internal dependencies
    "TaxoStore.Core\TaxoStore.Core.csproj",
    "Cadmus.TaxoStore.Parts\Cadmus.TaxoStore.Parts.csproj",
    # Layer 1: depends on Layer 0
    "TaxoStore.PgSql\TaxoStore.PgSql.csproj",                   # -> TaxoStore.Core
    "Cadmus.Seed.TaxoStore.Parts\Cadmus.Seed.TaxoStore.Parts.csproj", # -> Cadmus.TaxoStore.Parts
    # Layer 2: depends on Layer 1
    "TaxoStore.Api.Controllers\TaxoStore.Api.Controllers.csproj" # -> TaxoStore.Core, TaxoStore.PgSql
)
#endregion

#region REUSABLE LOGIC - No changes needed below this line
# ============================================================

# Function to clear NuGet caches to ensure fresh package resolution
function Clear-NuGetCache {
    Write-Host "`n=== CLEARING NUGET HTTP CACHE ===" -ForegroundColor Yellow
    dotnet nuget locals http-cache --clear
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Failed to clear NuGet http-cache" -ForegroundColor Yellow
    }
}

# Function to pack and optionally push a single project
function Build-PackageProject {
    param(
        [string]$ProjectPath,
        [bool]$PushToLocal,
        [string]$Feed,
        [string]$NuGet
    )

    $fullPath = Join-Path $PSScriptRoot $ProjectPath

    if (-not (Test-Path $fullPath)) {
        Write-Host "WARNING: Project not found: $fullPath" -ForegroundColor Yellow
        return $true
    }

    Write-Host "`nPacking $ProjectPath" -ForegroundColor Green

    # Pack the project
    dotnet pack $fullPath -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        # First pack attempt failed - try with restore
        Write-Host "  Retrying with restore..." -ForegroundColor Yellow
        dotnet pack $fullPath -c Release
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to pack $ProjectPath" -ForegroundColor Red
            return $false
        }
    }

    # Push to local feed immediately after successful pack
    if ($PushToLocal) {
        $projDir = Split-Path $fullPath
        $nupkgs = Get-ChildItem "$projDir\bin\Release" -Filter *.nupkg -ErrorAction SilentlyContinue

        foreach ($pkg in $nupkgs) {
            Write-Host "  -> Adding $($pkg.Name) to local feed" -ForegroundColor Cyan
            & $NuGet add $pkg.FullName -Source $Feed
            if ($LASTEXITCODE -ne 0) {
                Write-Host "WARNING: Failed to add $($pkg.Name) to local feed (may already exist)" -ForegroundColor Yellow
            }
        }

        # Clear http-cache after adding to ensure next project sees the new package
        dotnet nuget locals http-cache --clear | Out-Null
    }

    return $true
}

# Main execution
if ($Pack) {
    # Clear NuGet cache at the start to ensure clean state
    if ($PushLocal) {
        Clear-NuGetCache
    }

    Write-Host "`n=== PACKING PROJECTS IN DEPENDENCY ORDER ===" -ForegroundColor Yellow

    foreach ($projPath in $projectOrder) {
        $success = Build-PackageProject -ProjectPath $projPath -PushToLocal $PushLocal -Feed $LocalFeed -NuGet $NuGetExe
        if (-not $success) {
            exit 1
        }
    }
}

# Push to NuGet.org (separate step, typically after local testing)
if ($PushNuGet) {
    Write-Host "`n=== PUSHING TO NUGET.ORG ===" -ForegroundColor Yellow

    foreach ($projPath in $projectOrder) {
        $fullPath = Join-Path $PSScriptRoot $projPath

        if (Test-Path $fullPath) {
            $projDir = Split-Path $fullPath
            $nupkgs = Get-ChildItem "$projDir\bin\Release" -Filter *.nupkg -ErrorAction SilentlyContinue

            foreach ($pkg in $nupkgs) {
                Write-Host "Pushing $($pkg.Name) to NuGet.org" -ForegroundColor Green
                & $NuGetExe push $pkg.FullName -Source https://api.nuget.org/v3/index.json -SkipDuplicate
            }
        }
    }
}
#endregion

Write-Host "`nDone." -ForegroundColor Cyan
