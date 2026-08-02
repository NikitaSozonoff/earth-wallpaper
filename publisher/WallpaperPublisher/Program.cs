using WallpaperPublisher;

var command = args.FirstOrDefault(arg => !arg.StartsWith('-'))?.ToLowerInvariant() ?? "build";
if (command is not ("build" or "validate"))
{
    Console.Error.WriteLine("Usage: WallpaperPublisher [build|validate] [--config path]");
    return 64;
}

var configArgument = Array.FindIndex(args, arg => arg.Equals("--config", StringComparison.OrdinalIgnoreCase));
var configPath = configArgument >= 0 && configArgument + 1 < args.Length
    ? args[configArgument + 1]
    : Path.Combine(Environment.CurrentDirectory, "publisher.config.json");
configPath = Path.GetFullPath(configPath);

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Configuration file not found: {configPath}");
    return 66;
}

try
{
    var config = PublisherConfig.Load(configPath).Resolve(configPath);
    var logger = new PublisherLog(config.StatePath);
    logger.Info("run_started", $"Publisher command '{command}' started.");
    var result = new PublisherEngine(config, logger).Run(command);
    if (result.ManifestPath is not null) Console.WriteLine($"Manifest: {result.ManifestPath}");
    return result.Success ? 0 : 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Publisher failed: {exception.Message}");
    return 1;
}
