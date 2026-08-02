[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$RequireInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must look like 0.1.0 or 0.1.0-beta.1."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repositoryRoot "app\WallpaperWidget\WallpaperWidget.csproj"
$releaseBase = Join-Path $repositoryRoot "artifacts\release"
$releaseRoot = Join-Path $releaseBase $Version
$publishPath = Join-Path $releaseRoot "publish"
$portablePath = Join-Path $releaseRoot "EarthWallpaper-Portable-$Version.zip"
$installerScript = Join-Path $repositoryRoot "installer\EarthWallpaper.iss"

if (-not $releaseRoot.StartsWith($releaseBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved release output escaped the artifacts directory."
}
if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
$dotnet = if ($null -ne $dotnetCommand) { $dotnetCommand.Source } else { $null }
if (-not $dotnet)
{
    $dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (-not (Test-Path -LiteralPath $dotnet)) { throw ".NET SDK was not found." }
}

Write-Host "Publishing Earth Wallpaper $Version (win-x64)..." -ForegroundColor Cyan
& $dotnet publish $projectPath -c Release -r win-x64 --self-contained true --output $publishPath "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Get-ChildItem -LiteralPath $publishPath -Recurse -Filter "*.pdb" -File | Remove-Item -Force

$packagedExecutable = Join-Path $publishPath "EarthWallpaper.exe"
& $packagedExecutable --smoke-test
if ($LASTEXITCODE -ne 0) { throw "Packaged application smoke test failed with exit code $LASTEXITCODE." }

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $portablePath -CompressionLevel Optimal

$isccCommand = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
$iscc = if ($null -ne $isccCommand) { $isccCommand.Source } else { $null }
if (-not $iscc)
{
    $iscc = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

if ($iscc)
{
    Write-Host "Building Windows installer..." -ForegroundColor Cyan
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishPath" "/DOutputDir=$releaseRoot" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
}
elseif ($RequireInstaller)
{
    throw "Inno Setup compiler (ISCC.exe) was not found."
}
else
{
    Write-Warning "Inno Setup was not found; the portable ZIP was built without an installer."
}

$releaseFiles = Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Name -notin @("checksums.txt", "release-manifest.json") }
$checksumLines = foreach ($file in $releaseFiles)
{
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $releaseRoot "checksums.txt"), $checksumLines, [Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    version = $Version
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runtime = "win-x64"
    selfContained = $true
    files = @(Get-ChildItem -LiteralPath $releaseRoot -File | ForEach-Object {
        [ordered]@{ name = $_.Name; bytes = $_.Length }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "release-manifest.json") -Encoding UTF8

Write-Host "Release artifacts: $releaseRoot" -ForegroundColor Green
