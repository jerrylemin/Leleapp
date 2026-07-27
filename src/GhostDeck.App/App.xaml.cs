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
        InitializeComponent();
        var services = new ServiceCollection();
        services.AddSingleton<ProcessTelemetryService>();
        services.AddSingleton<PowerActionImporter>();
        services.AddSingleton<NetworkInfoService>();
        services.AddSingleton<CleanupService>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
