using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Extraction;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Services;
using NetDownloader.Core.Storage;
using NetDownloader.Gui.ViewModels;

namespace NetDownloader.Gui;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowVm
            };

            var engine = Services.GetRequiredService<IDownloadEngine>();
            var cnlServer = Services.GetRequiredService<IClickAndLoadServer>();

            _ = Task.Run(async () =>
            {
                try
                {
                    await engine.InitializeAsync();
                    await cnlServer.StartAsync();
                    mainWindowVm.RefreshPackages();
                }
                catch (Exception ex)
                {
                    var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_error.log");
                    System.IO.File.WriteAllText(logPath, ex.ToString());
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IFileStorageService, FileStorageService>();
        services.AddSingleton<IStateStore, JsonStateStore>();
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<ILinkGrabberService, LinkGrabberService>();
        services.AddSingleton<IClickAndLoadServer, ClickAndLoadServer>();
        services.AddSingleton<IDownloadEngine, DownloadEngine>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
    }
}
