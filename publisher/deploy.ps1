[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$Yes,
    [switch]$SkipPublicVerification
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$publisherRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$deployConfigPath = Join-Path $publisherRoot "deploy.config.json"
$publisherConfigPath = Join-Path $publisherRoot "publisher.config.json"
$statePath = Join-Path $publisherRoot "state"
$logPath = Join-Path $statePath "logs"
$reportPath = Join-Path $statePath "reports"
$planPath = Join-Path $statePath "plans"
$startedAt = [DateTimeOffset]::UtcNow
$runId = $startedAt.ToString("yyyyMMdd-HHmmss-fff")
$eventLogFile = Join-Path $logPath ("deploy-{0}.jsonl" -f $startedAt.ToString("yyyy-MM-dd"))
$rcloneLogFile = Join-Path $logPath ("rclone-{0}.log" -f $runId)
$reportFile = Join-Path $reportPath ("deploy-{0}.json" -f $runId)
$latestReportFile = Join-Path $reportPath "latest-deploy.json"
$script:Report = [ordered]@{
    runId = $runId
    startedAtUtc = $startedAt.ToString("o")
    finishedAtUtc = $null
    mode = if ($DryRun) { "dry-run" } else { "publish" }
    success = $false
    remote = $null
    bucket = $null
    publicBaseUrl = $null
    manifests = @()
    plannedAssets = 0
    plannedCatalogs = 0
    error = $null
}

New-Item -ItemType Directory -Force -Path $logPath, $reportPath, $planPath | Out-Null

function Write-DeployEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Level,
        [Parameter(Mandatory = $true)][string]$Event,
        [Parameter(Mandatory = $true)][string]$Message,
        [object]$Data
    )

    $record = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
        level = $Level
        event = $Event
        message = $Message
    }
    if ($null -ne $Data) { $record.data = $Data }
    Add-Content -LiteralPath $eventLogFile -Value ($record | ConvertTo-Json -Depth 8 -Compress) -Encoding UTF8

    $color = switch ($Level) {
        "error" { "Red" }
        "warning" { "Yellow" }
        "success" { "Green" }
        default { "Cyan" }
    }
    Write-Host $Message -ForegroundColor $color
}

function Save-DeployReport {
    $script:Report.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $json = $script:Report | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $reportFile -Value $json -Encoding UTF8
    Set-Content -LiteralPath $latestReportFile -Value $json -Encoding UTF8
}

function Get-RequiredString {
    param([object]$Object, [string]$PropertyName, [string]$Source)
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Required value '$PropertyName' is missing in $Source."
    }
    return ([string]$property.Value).Trim()
}

function Resolve-Rclone {
    $command = Get-Command "rclone" -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $fallback = Join-Path $env:LOCALAPPDATA "Programs\rclone\rclone.exe"
    if (Test-Path -LiteralPath $fallback -PathType Leaf) { return $fallback }
    throw "rclone was not found. Add its directory to PATH or place it at '$fallback'."
}

function Resolve-Dotnet {
    $command = Get-Command "dotnet" -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $fallback = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $fallback -PathType Leaf) { return $fallback }
    throw "The .NET SDK was not found. Install .NET 10 SDK or add dotnet to PATH."
}

function Invoke-Rclone {
    param(
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-DeployEvent "info" "rclone_started" $Operation $null
    & $script:RclonePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "rclone failed during '$Operation' with exit code $LASTEXITCODE. See $rcloneLogFile"
    }
    Write-DeployEvent "success" "rclone_finished" "$Operation completed." $null
}

function Get-Sha256 {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Test-LocalRelease {
    param([string]$OutputPath)

    $summaries = @()
    $assetPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $catalogPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($manifestName in @("manifest.json", "manifest-aesthetic.json")) {
        $manifestPath = Join-Path $OutputPath $manifestName
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Missing release file: $manifestPath"
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $catalogRelativePath = Get-RequiredString $manifest.catalog "path" $manifestName
        [void]$catalogPaths.Add($catalogRelativePath)
        $catalogPath = Join-Path $OutputPath ($catalogRelativePath.Replace("/", [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
            throw "Catalog referenced by $manifestName does not exist: $catalogPath"
        }

        $catalogBytes = [IO.File]::ReadAllBytes($catalogPath)
        if ($catalogBytes.LongLength -ne [long]$manifest.catalog.bytes) {
            throw "Catalog size mismatch for $catalogRelativePath."
        }
        $catalogHash = Get-Sha256 $catalogBytes
        if ($catalogHash -ne ([string]$manifest.catalog.sha256).ToLowerInvariant()) {
            throw "Catalog SHA-256 mismatch for $catalogRelativePath."
        }

        $catalog = [Text.Encoding]::UTF8.GetString($catalogBytes) | ConvertFrom-Json
        foreach ($entry in $catalog.entries) {
            $assetRelativePath = Get-RequiredString $entry "imageFile" $catalogRelativePath
            [void]$assetPaths.Add($assetRelativePath)
            $assetPath = Join-Path $OutputPath ($assetRelativePath.Replace("/", [IO.Path]::DirectorySeparatorChar))
            if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                throw "Asset referenced by $catalogRelativePath does not exist: $assetPath"
            }
            if ((Get-Item -LiteralPath $assetPath).Length -ne [long]$entry.imageBytes) {
                throw "Asset size mismatch: $assetRelativePath"
            }
        }

        $summaries += [ordered]@{
            name = $manifestName
            packId = [string]$manifest.packId
            contentVersion = [string]$manifest.contentVersion
            entryCount = [int]$manifest.entryCount
            downloadBytes = [long]$manifest.downloadBytes
            catalogPath = $catalogRelativePath
            catalogSha256 = $catalogHash
        }
    }
    $script:ReleaseAssetPaths = @($assetPaths | Sort-Object)
    $script:ReleaseCatalogPaths = @($catalogPaths | Sort-Object)
    return $summaries
}

function Test-PublicRelease {
    param([string]$PublicBaseUrl, [object[]]$ManifestSummaries)

    $http = [System.Net.Http.HttpClient]::new()
    $http.DefaultRequestHeaders.CacheControl = [System.Net.Http.Headers.CacheControlHeaderValue]::new()
    $http.DefaultRequestHeaders.CacheControl.NoCache = $true
    try {
        foreach ($summary in $ManifestSummaries) {
            $nonce = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            $manifestUrl = "$($PublicBaseUrl.TrimEnd('/'))/$($summary.name)?deployVerify=$nonce"
            $manifestBytes = $http.GetByteArrayAsync($manifestUrl).GetAwaiter().GetResult()
            $remoteManifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
            if ([string]$remoteManifest.contentVersion -ne [string]$summary.contentVersion) {
                throw "Public $($summary.name) reports content version '$($remoteManifest.contentVersion)', expected '$($summary.contentVersion)'."
            }

            $catalogUrl = "$($PublicBaseUrl.TrimEnd('/'))/$($summary.catalogPath)?deployVerify=$nonce"
            $catalogBytes = $http.GetByteArrayAsync($catalogUrl).GetAwaiter().GetResult()
            if ((Get-Sha256 $catalogBytes) -ne [string]$summary.catalogSha256) {
                throw "Public catalog SHA-256 mismatch: $($summary.catalogPath)"
            }
            Write-DeployEvent "success" "public_manifest_verified" "Verified public $($summary.name), version $($summary.contentVersion)." $null
        }
    }
    finally {
        $http.Dispose()
    }
}

try {
    if (-not (Test-Path -LiteralPath $deployConfigPath -PathType Leaf)) {
        throw "Missing local deployment config '$deployConfigPath'. Copy deploy.config.example.json and fill in local values."
    }
    $deployConfig = Get-Content -LiteralPath $deployConfigPath -Raw | ConvertFrom-Json
    $remoteName = Get-RequiredString $deployConfig "remoteName" $deployConfigPath
    $bucket = Get-RequiredString $deployConfig "bucket" $deployConfigPath
    $publicBaseUrl = Get-RequiredString $deployConfig "publicBaseUrl" $deployConfigPath
    if (-not $publicBaseUrl.EndsWith("/")) { $publicBaseUrl += "/" }
    $script:Report.remote = $remoteName
    $script:Report.bucket = $bucket
    $script:Report.publicBaseUrl = $publicBaseUrl

    $script:RclonePath = Resolve-Rclone
    $dotnetPath = Resolve-Dotnet
    Write-DeployEvent "info" "deployment_started" "Content deployment started in $($script:Report.mode) mode." @{ remote = $remoteName; bucket = $bucket }

    $configuredRemotes = & $script:RclonePath listremotes
    if ($LASTEXITCODE -ne 0 -or $configuredRemotes -notcontains "${remoteName}:") {
        throw "rclone remote '${remoteName}:' is not configured for the current Windows user."
    }

    if (-not $SkipBuild) {
        Write-DeployEvent "info" "publisher_started" "Validating sources and building the content release." $null
        & $dotnetPath run --project (Join-Path $publisherRoot "WallpaperPublisher\WallpaperPublisher.csproj") -- build --config $publisherConfigPath
        if ($LASTEXITCODE -ne 0) { throw "Publisher build failed with exit code $LASTEXITCODE." }
        Write-DeployEvent "success" "publisher_finished" "Content release built successfully." $null
    }

    $publisherConfig = Get-Content -LiteralPath $publisherConfigPath -Raw | ConvertFrom-Json
    $outputPath = [IO.Path]::GetFullPath((Join-Path $publisherRoot (Get-RequiredString $publisherConfig "outputPath" $publisherConfigPath)))
    $manifestSummaries = @(Test-LocalRelease $outputPath)
    $script:Report.manifests = $manifestSummaries
    $script:Report.plannedAssets = $script:ReleaseAssetPaths.Count
    $script:Report.plannedCatalogs = $script:ReleaseCatalogPaths.Count

    $assetsPlanFile = Join-Path $planPath "latest-assets.txt"
    $catalogsPlanFile = Join-Path $planPath "latest-catalogs.txt"
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllLines($assetsPlanFile, [string[]]$script:ReleaseAssetPaths, $utf8NoBom)
    [IO.File]::WriteAllLines($catalogsPlanFile, [string[]]$script:ReleaseCatalogPaths, $utf8NoBom)
    Write-DeployEvent "success" "release_validated" "Local release is internally consistent: $($script:ReleaseAssetPaths.Count) assets and $($script:ReleaseCatalogPaths.Count) catalogs are referenced." @{ manifests = $manifestSummaries }

    if (-not $DryRun -and -not $Yes) {
        Write-Host ""
        Write-Host "Remote:  ${remoteName}:$bucket" -ForegroundColor White
        Write-Host "Public:  $publicBaseUrl" -ForegroundColor White
        Write-Host "No remote files will be deleted; manifests will be replaced last." -ForegroundColor Yellow
        $confirmation = Read-Host "Type PUBLISH to continue"
        if ($confirmation -cne "PUBLISH") { throw "Deployment cancelled by user." }
    }

    $remoteRoot = "${remoteName}:$bucket"
    $commonArguments = @(
        "--s3-no-check-bucket",
        "--transfers", "4",
        "--checkers", "8",
        "--stats", "15s",
        "--log-level", "INFO",
        "--log-file", $rcloneLogFile
    )
    if ($DryRun) { $commonArguments += "--dry-run" }

    $immutableArguments = @(
        "--immutable",
        "--checksum",
        "--metadata-set", "cache-control=public, max-age=31536000, immutable"
    ) + $commonArguments

    Invoke-Rclone "Uploading referenced immutable assets" (@("copy", $outputPath, $remoteRoot, "--files-from", $assetsPlanFile) + $immutableArguments)
    Invoke-Rclone "Uploading referenced immutable catalogs" (@("copy", $outputPath, $remoteRoot, "--files-from", $catalogsPlanFile) + $immutableArguments)

    $manifestArguments = @(
        "--ignore-times",
        "--metadata-set", "cache-control=no-cache, max-age=0",
        "--metadata-set", "content-type=application/json"
    ) + $commonArguments

    foreach ($manifestName in @("manifest.json", "manifest-aesthetic.json")) {
        Invoke-Rclone "Publishing $manifestName" (@("copyto", (Join-Path $outputPath $manifestName), "$remoteRoot/$manifestName") + $manifestArguments)
    }

    if ($DryRun) {
        Write-DeployEvent "success" "dry_run_finished" "Dry run completed; R2 was not changed." $null
    }
    elseif (-not $SkipPublicVerification) {
        Test-PublicRelease $publicBaseUrl $manifestSummaries
    }

    $script:Report.success = $true
    Write-DeployEvent "success" "deployment_finished" "Content deployment completed successfully." $null
    Save-DeployReport
    exit 0
}
catch {
    $script:Report.error = $_.Exception.Message
    Write-DeployEvent "error" "deployment_failed" $_.Exception.Message $null
    Save-DeployReport
    Write-Host "Report: $latestReportFile" -ForegroundColor DarkGray
    exit 1
}
