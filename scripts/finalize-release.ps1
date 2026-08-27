[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ReleaseRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must look like 0.1.0 or 0.1.0-beta.1."
}

$resolvedRoot = [IO.Path]::GetFullPath($ReleaseRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Release directory was not found: $resolvedRoot"
}

$checksumPath = Join-Path $resolvedRoot "checksums.txt"
$manifestPath = Join-Path $resolvedRoot "release-manifest.json"
Remove-Item -LiteralPath $checksumPath, $manifestPath -Force -ErrorAction SilentlyContinue

$files = @(Get-ChildItem -LiteralPath $resolvedRoot -File | Sort-Object Name)
if ($files.Count -eq 0) { throw "The release directory contains no artifacts." }

$checksumLines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    version = $Version
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    files = @($files | ForEach-Object {
        [ordered]@{ name = $_.Name; bytes = $_.Length }
    })
}
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 5),
    [Text.UTF8Encoding]::new($false))

Write-Host "Finalized $($files.Count) release artifacts under $resolvedRoot." -ForegroundColor Green
