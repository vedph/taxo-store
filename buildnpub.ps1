# BUILD AND PUBLISH SCRIPT FOR .NET PROJECTS
# - to pack all packages:
#   .\buildnpub.ps1 -Pack
# - to pack and push locally:
#   .\buildnpub.ps1 -Pack -PushLocal
# - to pack and push to NuGet.org:
#   .\buildnpub.ps1 -Pack -PushNuGet
# - to pack and push to both:
#   .\buildnpub.ps1 -Pack -PushLocal -PushNuGet
# - to push the packages left by the last -Pack run without rebuilding:
#   .\buildnpub.ps1 -PushLocal and/or -PushNuGet
#
# To reuse in another solution, copy this script into the solution root and
# edit the CONFIGURATION region only.
#
# How stale dependencies are avoided:
# - restore runs with the same configuration used for packing, as projects
#   reference internal dependencies as projects in Debug and as packages in
#   Release, so a default (Debug) restore would not resolve them as packages;
# - each package just built is evicted from the NuGet global packages folder,
#   so that its dependents cannot pick a copy cached by an earlier build of
#   the same version;
# - each package just built is copied into a staging folder used as an
#   additional restore source, so that dependents find it even when it is not
#   (yet) available from the local feed or from NuGet.org.

param(
    [switch]$Pack,
    [switch]$PushLocal,
    [switch]$PushNuGet,
    [string]$LocalFeed = "C:\Projects\_NuGet",
    [string]$NuGetExe = "C:\Exe\nuget.exe",
    [string]$Configuration = "Release",
    [string]$NuGetSource = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"

Write-Host "Build & Publish Script" -ForegroundColor Cyan

#region CONFIGURATION - Customize this section for each solution
# ============================================================
# Projects to pack, as paths relative to this script. They can be listed in
# any order: the script sorts them so that each project is packed after the
# internal packages it depends on.
$projects = @(
    "TaxoStore.Core\TaxoStore.Core.csproj",
    "Cadmus.TaxoStore.Parts\Cadmus.TaxoStore.Parts.csproj",
    "TaxoStore.PgSql\TaxoStore.PgSql.csproj",
    "Cadmus.Seed.TaxoStore.Parts\Cadmus.Seed.TaxoStore.Parts.csproj",
    "TaxoStore.Cli.Core\TaxoStore.Cli.Core.csproj",
    "TaxoStore.Api.Controllers\TaxoStore.Api.Controllers.csproj"
)
#endregion

#region FUNCTIONS
function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) {
        throw "$what failed (exit code $LASTEXITCODE)"
    }
}

# Get the package ID of a project: its PackageId if any, else its file name.
function Get-ProjectPackageId([string]$path) {
    [xml]$xml = Get-Content $path -Raw
    $node = $xml.SelectSingleNode("//*[local-name()='PackageId']")
    if ($node -and $node.InnerText.Trim()) {
        return $node.InnerText.Trim()
    }
    return [IO.Path]::GetFileNameWithoutExtension($path)
}

# Get the names of all the packages and projects referenced by a project,
# whatever the configuration they are conditioned to.
function Get-ProjectReferenceNames([string]$path) {
    [xml]$xml = Get-Content $path -Raw
    $xpath = "//*[local-name()='PackageReference' or local-name()='ProjectReference']"
    foreach ($node in $xml.SelectNodes($xpath)) {
        $include = $node.GetAttribute("Include")
        if (-not $include) { continue }
        if ($node.LocalName -eq "ProjectReference") {
            [IO.Path]::GetFileNameWithoutExtension($include)
        } else {
            $include
        }
    }
}

# Sort projects so that each one comes after its internal dependencies.
function Get-SortedProjects([string[]]$paths) {
    $items = @()
    foreach ($path in $paths) {
        $fullPath = Join-Path $PSScriptRoot $path
        if (-not (Test-Path $fullPath)) {
            throw "Project not found: $fullPath"
        }
        $items += [pscustomobject]@{
            Path = $fullPath
            Id   = Get-ProjectPackageId $fullPath
            Deps = @()
        }
    }
    foreach ($item in $items) {
        $refs = @(Get-ProjectReferenceNames $item.Path)
        $item.Deps = @($items | Where-Object {
            $_.Id -ne $item.Id -and $refs -contains $_.Id
        } | ForEach-Object { $_.Id })
    }

    $sorted = @()
    $pending = $items
    while ($pending.Count -gt 0) {
        $done = @($sorted | ForEach-Object { $_.Id })
        $ready = @($pending | Where-Object {
            @($_.Deps | Where-Object { $done -notcontains $_ }).Count -eq 0
        })
        if ($ready.Count -eq 0) {
            $ids = ($pending | ForEach-Object { $_.Id }) -join ", "
            throw "Circular dependency among projects: $ids"
        }
        $sorted += $ready
        $pending = @($pending | Where-Object { $ready -notcontains $_ })
    }
    return $sorted
}

# Read the ID and version of a package from its nuspec.
function Get-PackageIdentity([string]$nupkgPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $entry = $zip.Entries | Where-Object {
            $_.FullName -notmatch "[\\/]" -and $_.FullName -like "*.nuspec"
        } | Select-Object -First 1
        if (-not $entry) { throw "No nuspec found in $nupkgPath" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally {
        $zip.Dispose()
    }
    return [pscustomobject]@{
        Id      = [string]$nuspec.package.metadata.id
        Version = [string]$nuspec.package.metadata.version
    }
}

function Get-GlobalPackagesFolder {
    $lines = @(dotnet nuget locals global-packages --list)
    Assert-ExitCode "Locating the NuGet global packages folder"
    $line = $lines | Where-Object { $_ -match "^\s*global-packages:" } | Select-Object -First 1
    if (-not $line) { throw "Cannot locate the NuGet global packages folder" }
    return ($line -replace "^\s*global-packages:\s*", "").Trim()
}

# Remove a package version from a folder using the id/version layout (the
# global packages folder, or a local feed populated by "nuget add").
function Remove-PackageFolder([string]$root, $identity) {
    $dir = Join-Path (Join-Path $root $identity.Id.ToLowerInvariant()) `
        $identity.Version.ToLowerInvariant()
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
        return $true
    }
    return $false
}

function Get-NuGetExe {
    if (Test-Path $NuGetExe) { return $NuGetExe }
    $cmd = Get-Command nuget -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "nuget.exe not found at '$NuGetExe' nor in PATH: use -NuGetExe to specify its location"
}

function Get-ProjectPackages([string]$projectPath) {
    $dir = Join-Path (Split-Path $projectPath) "bin\$Configuration"
    return @(Get-ChildItem $dir -Filter *.nupkg -ErrorAction SilentlyContinue)
}

function Push-ToLocalFeed($pkg) {
    # remove any copy of this version first, as "nuget add" does not overwrite
    # and would silently leave the old build in the feed
    if (Remove-PackageFolder $LocalFeed (Get-PackageIdentity $pkg.FullName)) {
        Write-Host "  -> Replacing $($pkg.Name) in local feed" -ForegroundColor Cyan
    } else {
        Write-Host "  -> Adding $($pkg.Name) to local feed" -ForegroundColor Cyan
    }
    & $nuget add $pkg.FullName -Source $LocalFeed
    Assert-ExitCode "Adding $($pkg.Name) to local feed"
}

function Push-ToNuGet($pkg) {
    Write-Host "  -> Pushing $($pkg.Name) to NuGet.org" -ForegroundColor Cyan
    & $nuget push $pkg.FullName -Source $NuGetSource -SkipDuplicate
    Assert-ExitCode "Pushing $($pkg.Name) to NuGet.org"
}
#endregion

if (-not ($Pack -or $PushLocal -or $PushNuGet)) {
    Write-Host "Nothing to do: specify -Pack, -PushLocal and/or -PushNuGet." -ForegroundColor Yellow
    exit 0
}

$staging = $null
try {
    $sortedProjects = Get-SortedProjects $projects

    if ($PushLocal -or $PushNuGet) {
        $nuget = Get-NuGetExe
    }
    if ($PushLocal -and -not (Test-Path $LocalFeed)) {
        New-Item -ItemType Directory -Path $LocalFeed | Out-Null
    }

    if ($Pack) {
        Write-Host "`n=== PACKING PROJECTS IN DEPENDENCY ORDER ===" -ForegroundColor Yellow
        foreach ($project in $sortedProjects) {
            $deps = ""
            if ($project.Deps.Count -gt 0) { $deps = " -> " + ($project.Deps -join ", ") }
            Write-Host "  $($project.Id)$deps"
        }

        $globalPackages = Get-GlobalPackagesFolder
        $staging = Join-Path ([IO.Path]::GetTempPath()) ("buildnpub-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $staging | Out-Null

        foreach ($project in $sortedProjects) {
            Write-Host "`nPacking $($project.Id)" -ForegroundColor Green

            # Remove packages left over from previous runs so old versions
            # don't accumulate and get re-pushed alongside the new one.
            $releaseDir = Join-Path (Split-Path $project.Path) "bin\$Configuration"
            if (Test-Path $releaseDir) {
                Get-ChildItem $releaseDir -File |
                    Where-Object { $_.Extension -in ".nupkg", ".snupkg" } |
                    Remove-Item -Force
            }

            # Restore for the packing configuration, ignoring the outcome of
            # any previous restore, and adding the packages built so far.
            dotnet restore $project.Path --force `
                "-p:Configuration=$Configuration" `
                "-p:RestoreAdditionalProjectSources=$staging"
            Assert-ExitCode "Restoring $($project.Id)"

            dotnet pack $project.Path -c $Configuration --no-restore
            Assert-ExitCode "Packing $($project.Id)"

            $nupkgs = Get-ProjectPackages $project.Path
            if ($nupkgs.Count -eq 0) {
                throw "No package found in $releaseDir after packing $($project.Id)"
            }

            foreach ($pkg in $nupkgs) {
                $identity = Get-PackageIdentity $pkg.FullName
                if (Remove-PackageFolder $globalPackages $identity) {
                    Write-Host "  -> Evicted $($identity.Id) $($identity.Version) from global packages" `
                        -ForegroundColor DarkGray
                }
                Copy-Item $pkg.FullName $staging

                # push right after packing, so that dependents can restore it
                if ($PushLocal) { Push-ToLocalFeed $pkg }
                if ($PushNuGet) { Push-ToNuGet $pkg }
            }
        }
    }
    else {
        Write-Host "`n=== PUSHING EXISTING PACKAGES ===" -ForegroundColor Yellow

        foreach ($project in $sortedProjects) {
            $nupkgs = Get-ProjectPackages $project.Path
            if ($nupkgs.Count -eq 0) {
                Write-Host "WARNING: no package found for $($project.Id): run with -Pack first" `
                    -ForegroundColor Yellow
                continue
            }
            Write-Host "`n$($project.Id)" -ForegroundColor Green
            foreach ($pkg in $nupkgs) {
                if ($PushLocal) { Push-ToLocalFeed $pkg }
                if ($PushNuGet) { Push-ToNuGet $pkg }
            }
        }
    }
}
catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($staging -and (Test-Path $staging)) {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`nDone." -ForegroundColor Cyan
