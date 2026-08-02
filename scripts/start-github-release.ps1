[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Version)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Invalid release version." }

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $scriptRoot
$gitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue
$git = if ($null -ne $gitCommand) { $gitCommand.Source } else { $null }
if (-not $git) { $git = "C:\Program Files\Git\cmd\git.exe" }
$safeDirectory = $repositoryRoot.Replace('\', '/')

$status = & $git -c "safe.directory=$safeDirectory" -C $repositoryRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw "Git status failed." }
if ($status) { throw "The working tree is not clean. Commit and push Stage 6 before creating a release." }

$branch = & $git -c "safe.directory=$safeDirectory" -C $repositoryRoot branch --show-current
if ($branch.Trim() -ne "main") { throw "Application releases must be started from main." }

$tag = "v$Version"
& $git -c "safe.directory=$safeDirectory" -C $repositoryRoot rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Tag $tag already exists locally." }

Write-Host "This will create and push $tag. GitHub Actions will build the installer and publish the release." -ForegroundColor Yellow
$confirmation = Read-Host "Type RELEASE to continue"
if ($confirmation -cne "RELEASE") { throw "Release cancelled." }

& $git -c "safe.directory=$safeDirectory" -C $repositoryRoot tag -a $tag -m "Earth Wallpaper $Version"
if ($LASTEXITCODE -ne 0) { throw "Git tag creation failed." }
& $git -c "safe.directory=$safeDirectory" -C $repositoryRoot push origin $tag
if ($LASTEXITCODE -ne 0) { throw "Git tag push failed. The local tag remains available for retry." }

Write-Host "Release workflow started for $tag." -ForegroundColor Green
