param(
    [switch]$ValidateOnly
)

$publisherRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$command = if ($ValidateOnly) { "validate" } else { "build" }

& dotnet run --project "$publisherRoot\WallpaperPublisher\WallpaperPublisher.csproj" -- $command --config "$publisherRoot\publisher.config.json"
exit $LASTEXITCODE
