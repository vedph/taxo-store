# BUMP VERSION
# This script updates the version number in Directory.Build.props and Directory.Packages.props
# Usage: .\bumpver.ps1 -Version "1.2.3"

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

Write-Host "Bumping version to $Version" -ForegroundColor Cyan

# --- Update Directory.Build.props ---
$buildProps = "Directory.Build.props"

if (Test-Path $buildProps) {
    Write-Host "Updating $buildProps" -ForegroundColor Yellow
    $xml = [xml](Get-Content $buildProps)
    $xml.Project.PropertyGroup.Version = $Version
    $xml.Save($buildProps)
} else {
    Write-Host "ERROR: $buildProps not found." -ForegroundColor Red
    exit 1
}

# --- Update Directory.Packages.props ---
$packagesProps = "Directory.Packages.props"

if (Test-Path $packagesProps) {
    Write-Host "Updating $packagesProps" -ForegroundColor Yellow
    $xml = [xml](Get-Content $packagesProps)
    
    foreach ($packageVersion in $xml.Project.ItemGroup.PackageVersion) {
        $packageVersion.Version = $Version
    }
    
    $xml.Save($packagesProps)
} else {
    Write-Host "ERROR: $packagesProps not found." -ForegroundColor Red
    exit 1
}

Write-Host "Version bump completed." -ForegroundColor Green
