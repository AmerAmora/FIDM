using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;

namespace NetDownloader.Gui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDownloadEngine _engine;
    private readonly IStateStore _stateStore;

    [ObservableProperty]
    private int _maxConcurrentPackages = 3;

    [ObservableProperty]
    private int _maxDownloadsPerPackage = 5;

    [ObservableProperty]
    private int _chunksPerFile = 8;

    [ObservableProperty]
    private bool _isSpeedLimitEnabled;

    [ObservableProperty]
    private int _speedLimitMb = 50;

    [ObservableProperty]
    private string _defaultDownloadDirectory = string.Empty;

    [ObservableProperty]
    private bool _autoExtract = true;

    [ObservableProperty]
    private bool _deleteArchiveAfterExtract;

    [ObservableProperty]
    private string _saveStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSaveStatusVisible;

    public SettingsViewModel(IDownloadEngine engine, IStateStore stateStore)
    {
        _engine = engine;
        _stateStore = stateStore;
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var cfg = _engine.Configuration;
        MaxConcurrentPackages = cfg.MaxConcurrentPackages;
        MaxDownloadsPerPackage = cfg.MaxConcurrentDownloadsPerPackage;
        ChunksPerFile = cfg.ChunksPerFile;
        IsSpeedLimitEnabled = cfg.MaxSpeedBytesPerSecond > 0;
        SpeedLimitMb = cfg.MaxSpeedBytesPerSecond > 0 ? (int)(cfg.MaxSpeedBytesPerSecond / (1024 * 1024)) : 50;
        DefaultDownloadDirectory = cfg.DefaultDownloadDirectory;
        AutoExtract = cfg.AutoExtract;
        DeleteArchiveAfterExtract = cfg.DeleteArchiveAfterExtract;
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        var cfg = new EngineConfiguration
        {
            MaxConcurrentPackages = MaxConcurrentPackages,
            MaxConcurrentDownloadsPerPackage = MaxDownloadsPerPackage,
            ChunksPerFile = ChunksPerFile,
            MaxSpeedBytesPerSecond = IsSpeedLimitEnabled ? (long)SpeedLimitMb * 1024 * 1024 : 0,
            DefaultDownloadDirectory = DefaultDownloadDirectory,
            AutoExtract = AutoExtract,
            DeleteArchiveAfterExtract = DeleteArchiveAfterExtract
        };

        _engine.UpdateConfiguration(cfg);
        await _stateStore.SaveSettingsAsync(cfg);

        SaveStatusMessage = "Settings saved & applied";
        IsSaveStatusVisible = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsSaveStatusVisible = false);
        });
    }
}
