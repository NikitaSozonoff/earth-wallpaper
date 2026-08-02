using System.Text.Json;

namespace WallpaperPublisher;

public sealed class PublisherLog
{
    private readonly string _logPath;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..10];

    public PublisherLog(string statePath)
    {
        var logDirectory = Path.Combine(statePath, "logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"publisher-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
    }

    public void Info(string eventName, string message, object? data = null) => Write("info", eventName, message, data);
    public void Warning(string eventName, string message, object? data = null) => Write("warning", eventName, message, data);
    public void Error(string eventName, string message, object? data = null) => Write("error", eventName, message, data);

    private void Write(string level, string eventName, string message, object? data)
    {
        var record = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level,
            runId = _runId,
            eventName,
            message,
            data,
        };
        File.AppendAllText(_logPath, JsonSerializer.Serialize(record, JsonDefaults.Canonical) + Environment.NewLine);
        Console.WriteLine($"[{level.ToUpperInvariant()}] {message}");
    }
}
