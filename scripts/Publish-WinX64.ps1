[CmdletBinding()]
param(
    [switch]$SkipFormat,
    [switch]$SkipTests,
    [switch]$SkipPublishedAppSmokeTest,
    [switch]$SkipReleaseAssets,

    [ValidateSet('All', 'SelfContained', 'FrameworkDependent')]
    [string]$Variant = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'GitKeyRouter.sln'
$project = Join-Path $root 'src\GitKeyRouter.App\GitKeyRouter.App.csproj'
$selfContainedPublishDir = Join-Path $root 'artifacts\publish\win-x64'
$frameworkDependentPublishDir = Join-Path $root 'artifacts\publish\win-x64-framework-dependent'
$validationScript = Join-Path $PSScriptRoot 'Test-WinX64Publish.ps1'
$releaseScript = Join-Path $PSScriptRoot 'Prepare-ReleaseAssets.ps1'
$versionProps = Join-Path $root 'Directory.Build.props'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK was not found. This script will not install it automatically.'
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($Arguments -join ' ')"
    }
}

function Publish-Variant {
    param(
        [Parameter(Mandatory)][string]$Profile,
        [Parameter(Mandatory)][string]$PublishDir,
        [Parameter(Mandatory)][string]$ChecksumFileName
    )

    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }

    Invoke-DotNet -Arguments @(
        'publish',
        $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--no-restore',
        "-p:PublishProfile=$Profile",
        '-o', $PublishDir
    )

    $validationParameters = @{
        PublishDir = $PublishDir
        ChecksumFileName = $ChecksumFileName
    }

    if ($SkipPublishedAppSmokeTest) {
        $validationParameters.SkipLaunch = $true
    }

    & $validationScript @validationParameters
    Write-Host "Published $Profile to: $PublishDir"
}

Push-Location $root
try {
    Invoke-DotNet -Arguments @('restore', $solution)
    Invoke-DotNet -Arguments @('restore', $project, '-r', 'win-x64')

    if (-not $SkipFormat) {
        Invoke-DotNet -Arguments @('format', $solution)
    }

    Invoke-DotNet -Arguments @('build', $solution, '-c', 'Release', '--no-restore')

    if (-not $SkipTests) {
        Invoke-DotNet -Arguments @('test', $solution, '-c', 'Release', '--no-build')
    }

    if ($Variant -in @('All', 'SelfContained')) {
        Publish-Variant `
            -Profile 'win-x64-single-file' `
            -PublishDir $selfContainedPublishDir `
            -ChecksumFileName 'GitKeyRouter-win-x64.sha256'
    }

    if ($Variant -in @('All', 'FrameworkDependent')) {
        Publish-Variant `
            -Profile 'win-x64-framework-dependent' `
            -PublishDir $frameworkDependentPublishDir `
            -ChecksumFileName 'GitKeyRouter-win-x64-framework-dependent.sha256'
    }

    if ($Variant -eq 'All' -and -not $SkipReleaseAssets) {
        [xml]$props = Get-Content -LiteralPath $versionProps -Raw
        $version = [string]$props.Project.PropertyGroup.Version
        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "Version was not found in: $versionProps"
        }

        & $releaseScript -Version $version
        if ($LASTEXITCODE -ne 0) {
            throw "Release asset preparation failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Publish completed. Variant: $Variant"
    Write-Host "Self-contained: $selfContainedPublishDir"
    Write-Host "Framework-dependent: $frameworkDependentPublishDir"
}
finally {
    Pop-Location
}
