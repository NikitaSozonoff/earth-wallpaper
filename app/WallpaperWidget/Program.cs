using Avalonia;
using Avalonia.Threading;
using System.IO.Pipes;
using WallpaperWidget.Services;

namespace WallpaperWidget;

sealed class Program
{
    private const string MutexName = "EarthWallpaper.NikitaSozonoff";
    private const string ActivationPipeName = "EarthWallpaper.NikitaSozonoff.Activate";
    private static Mutex? _instanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = RunUiSmokeTest();
            return;
        }

        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = RunPackagedSmokeTest();
            return;
        }

        if (args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            try { new AutostartService().SetEnabled(false); } catch { }
            return;
        }

        _instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            _instanceMutex.Dispose();
            return;
        }

        _ = ListenForActivationAsync();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static async Task ListenForActivationAsync()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                Dispatcher.UIThread.Post(() => (Application.Current as App)?.ActivateFromExternalRequest());
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
                pipe.Connect(500);
                pipe.WriteByte(1);
                return;
            }
            catch { Thread.Sleep(200); }
        }
    }

    private static int RunPackagedSmokeTest()
    {
        try
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json"))) return 2;
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "remote-content.json"))) return 3;
            return SemanticVersion.TryParse(ApplicationVersion.Display, out _) ? 0 : 4;
        }
        catch { return 1; }
    }

    private static int RunUiSmokeTest()
    {
        try
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return RunPackagedSmokeTest();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 5;
        }
    }
}
