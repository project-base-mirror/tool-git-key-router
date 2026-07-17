[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$parent = Split-Path -Parent $root
$name = Split-Path -Leaf $root

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $parent 'GitKeyRouter-source-with-git.zip'
}

if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw 'tar.exe was not found. Windows 10/11 normally includes it. This script will not install software.'
}

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Push-Location $parent
try {
    & tar.exe -a -c -f $OutputPath `
        --exclude="$name/.vs" `
        --exclude="$name/**/bin" `
        --exclude="$name/**/obj" `
        --exclude="$name/artifacts" `
        $name

    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE"
    }

    Write-Host "Source archive created: $OutputPath"
    Write-Host 'The .git directory is included.'
}
finally {
    Pop-Location
}
