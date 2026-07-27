using System.Runtime.InteropServices;
using System.Text;
using GhostDeck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace GhostDeck.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Window? _window;

    public App()
    {
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartupFailure("AppDomain.CurrentDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteStartupFailure("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        try
        {
            InitializeComponent();

            var services = new ServiceCollection();
            services.AddSingleton<ProcessTelemetryService>();
            services.AddSingleton<PowerActionImporter>();
            services.AddSingleton<NetworkInfoService>();
            services.AddSingleton<CleanupService>();
            services.AddSingleton<MainWindow>();
            Services = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            ShowStartupFailure("GhostDeck failed while loading application resources.", ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = Services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
        catch (Exception ex)
        {
            ShowStartupFailure("GhostDeck failed while creating the main window.", ex);
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        WriteStartupFailure("Microsoft.UI.Xaml.Application.UnhandledException", args.Exception);
    }

    private static void ShowStartupFailure(string message, Exception exception)
    {
        var logPath = WriteStartupFailure(message, exception);
        var details = $"{message}\n\n{exception.GetType().FullName}\nHRESULT: 0x{exception.HResult:X8}\n{exception.Message}\n\nStartup log:\n{logPath}";
        MessageBox(IntPtr.Zero, details, "GhostDeck startup error", 0x00000010);
    }

    private static string WriteStartupFailure(string source, Exception? exception)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GhostDeck",
            "Logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "startup.log");
        var text = new StringBuilder()
            .AppendLine(new string('=', 80))
            .AppendLine(DateTimeOffset.Now.ToString("O"))
            .AppendLine(source)
            .AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}")
            .AppendLine($"OS: {RuntimeInformation.OSDescription}")
            .AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Base directory: {AppContext.BaseDirectory}");

        if (exception is null)
        {
            text.AppendLine("No exception object was supplied.");
        }
        else
        {
            var depth = 0;
            for (var current = exception; current is not null; current = current.InnerException)
            {
                text.AppendLine($"Exception level {depth}: {current.GetType().FullName}");
                text.AppendLine($"HRESULT: 0x{current.HResult:X8}");
                text.AppendLine($"Message: {current.Message}");
                text.AppendLine(current.StackTrace ?? "No stack trace.");
                depth++;
            }
        }

        File.AppendAllText(logPath, text.ToString(), Encoding.UTF8);
        return logPath;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
