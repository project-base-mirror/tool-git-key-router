[CmdletBinding()]
param(
    [switch]$SkipFormat,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GitKeyRouter.sln'
$project = Join-Path $root 'src\GitKeyRouter.App\GitKeyRouter.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish\win-x64'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK was not found. This script will not install it automatically.'
}

Push-Location $root
try {
    dotnet restore $solution

    if (-not $SkipFormat) {
        dotnet format $solution
    }

    dotnet build $solution -c Release --no-restore

    if (-not $SkipTests) {
        dotnet test $solution -c Release --no-build
    }

    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-build `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir

    Write-Host "Published to: $publishDir"
}
finally {
    Pop-Location
}
