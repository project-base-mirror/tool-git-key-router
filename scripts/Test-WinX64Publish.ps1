[CmdletBinding()]
param(
    [string]$PublishDir,
    [string]$ChecksumFileName = 'GitKeyRouter-win-x64.sha256',
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $root 'artifacts\publish\win-x64'
}

if ([string]::IsNullOrWhiteSpace($ChecksumFileName) -or
    [System.IO.Path]::GetFileName($ChecksumFileName) -ne $ChecksumFileName -or
    -not $ChecksumFileName.EndsWith('.sha256', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ChecksumFileName must be a .sha256 file name without a directory: $ChecksumFileName"
}

if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
    throw "Publish directory does not exist: $PublishDir"
}

$entries = @(Get-ChildItem -LiteralPath $PublishDir -Force)
$expectedExe = Join-Path $PublishDir 'GitKeyRouter.exe'

if ($entries.Count -ne 1) {
    $found = if ($entries.Count -eq 0) {
        '<empty>'
    }
    else {
        ($entries | ForEach-Object { $_.Name }) -join ', '
    }

    throw "Single-file validation failed. Expected only GitKeyRouter.exe, found: $found"
}

if ($entries[0].PSIsContainer -or -not (Test-Path -LiteralPath $expectedExe -PathType Leaf)) {
    throw "Single-file validation failed. Expected executable was not found: $expectedExe"
}

$executable = Get-Item -LiteralPath $expectedExe
if ($executable.Length -le 0) {
    throw 'Single-file validation failed. GitKeyRouter.exe is empty.'
}

$stream = [System.IO.File]::OpenRead($expectedExe)
try {
    $firstByte = $stream.ReadByte()
    $secondByte = $stream.ReadByte()
}
finally {
    $stream.Dispose()
}

if ($firstByte -ne 0x4D -or $secondByte -ne 0x5A) {
    throw 'Single-file validation failed. GitKeyRouter.exe does not have a valid Windows PE header.'
}

if (-not $SkipLaunch) {
    $process = Start-Process `
        -FilePath $expectedExe `
        -ArgumentList '--version' `
        -PassThru `
        -Wait

    if ($process.ExitCode -ne 0) {
        throw "Published application smoke test failed with exit code $($process.ExitCode)."
    }
}

$hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
$hashStream = [System.IO.File]::OpenRead($expectedExe)
try {
    $hashBytes = $hashAlgorithm.ComputeHash($hashStream)
}
finally {
    $hashStream.Dispose()
    $hashAlgorithm.Dispose()
}

$hash = ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
$checksumPath = Join-Path (Split-Path -Parent $PublishDir) $ChecksumFileName
$checksumContent = "$hash  GitKeyRouter.exe`r`n"
[System.IO.File]::WriteAllText($checksumPath, $checksumContent, [System.Text.Encoding]::ASCII)
$sizeMiB = [Math]::Round($executable.Length / 1MB, 2)

Write-Host 'Published artifact validation passed.'
Write-Host "File: $expectedExe"
Write-Host "Size: $sizeMiB MiB"
Write-Host "SHA-256: $hash"
Write-Host "Checksum: $checksumPath"
Write-Host "Launch test: $(if ($SkipLaunch) { 'skipped' } else { 'passed' })"
