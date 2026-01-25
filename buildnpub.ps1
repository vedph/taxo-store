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

# 1. Define projects in dependency order (no dependencies first, then dependents)
# Order determined by analyzing PackageReference elements in Release configuration
$projectOrder = @(
    "TaxoStore.Core\TaxoStore.Core.csproj",
    "TaxoStore.Api.Controllers\TaxoStore.Api.Controllers.csproj",
    "TaxoStore.PgSql\TaxoStore.PgSql.csproj"
)

if ($Pack) {
    Write-Host "`n=== PACKING PROJECTS IN DEPENDENCY ORDER ===" -ForegroundColor Yellow

    foreach ($projPath in $projectOrder) {
        $fullPath = Join-Path $PSScriptRoot $projPath

        if (Test-Path $fullPath) {
            Write-Host "`nPacking $projPath" -ForegroundColor Green
            dotnet pack $fullPath -c Release

            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Failed to pack $projPath" -ForegroundColor Red
                exit 1
            }

            # If pushing locally, add the package immediately after packing
            if ($PushLocal) {
                $projDir = Split-Path $fullPath
                $nupkgs = Get-ChildItem "$projDir\bin\Release" -Filter *.nupkg -ErrorAction SilentlyContinue

                foreach ($pkg in $nupkgs) {
                    Write-Host "  -> Adding $($pkg.Name) to local feed" -ForegroundColor Cyan
                    & $NuGetExe add $pkg.FullName -Source $LocalFeed
                }
            }
        } else {
            Write-Host "WARNING: Project not found: $fullPath" -ForegroundColor Yellow
        }
    }
}

# 2. Push to local feed (already done incrementally above if -PushLocal was specified with -Pack)
if ($PushLocal -and -not $Pack) {
    Write-Host "`n=== PUSHING TO LOCAL FEED ===" -ForegroundColor Yellow
    Write-Host "Note: Packages are already added incrementally during packing." -ForegroundColor Cyan
    Write-Host "This section only runs if you use -PushLocal without -Pack." -ForegroundColor Cyan
}

# 3. Push to NuGet.org
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

Write-Host "`nDone." -ForegroundColor Cyan
