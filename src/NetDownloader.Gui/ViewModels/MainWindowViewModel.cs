using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Models;
using NetDownloader.Core.Persistence;
using NetDownloader.Core.Services;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public enum NavigationPage
{
    Downloads,
    LinkGrabber,
    Completed,
    Queues,
    Scheduler,
    Settings
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDownloadEngine _engine;
    private readonly IStateStore _stateStore;
    private readonly ILinkGrabberService _linkGrabber;
    private readonly IClickAndLoadServer _clickAndLoadServer;

    [ObservableProperty]
    private NavigationPage _currentPage = NavigationPage.Downloads;

    public bool IsDownloadsView => CurrentPage == NavigationPage.Downloads;
    public bool IsLinkGrabberView => CurrentPage == NavigationPage.LinkGrabber;
    public bool IsCompletedView => CurrentPage == NavigationPage.Completed;
    public bool IsQueuesView => CurrentPage == NavigationPage.Queues;
    public bool IsSchedulerView => CurrentPage == NavigationPage.Scheduler;
    public bool IsSettingsView => CurrentPage == NavigationPage.Settings;

    public string DownloadsNavBackground => IsDownloadsView ? "#162033" : "Transparent";
    public string DownloadsNavForeground => IsDownloadsView ? "#F8FAFC" : "#8B98AD";
    public bool DownloadsIndicatorVisible => IsDownloadsView;

    public string LinkGrabberNavBackground => IsLinkGrabberView ? "#162033" : "Transparent";
    public string LinkGrabberNavForeground => IsLinkGrabberView ? "#F8FAFC" : "#8B98AD";
    public bool LinkGrabberIndicatorVisible => IsLinkGrabberView;

    public string CompletedNavBackground => IsCompletedView ? "#162033" : "Transparent";
    public string CompletedNavForeground => IsCompletedView ? "#F8FAFC" : "#8B98AD";
    public bool CompletedIndicatorVisible => IsCompletedView;

    public string QueuesNavBackground => IsQueuesView ? "#162033" : "Transparent";
    public string QueuesNavForeground => IsQueuesView ? "#F8FAFC" : "#8B98AD";
    public bool QueuesIndicatorVisible => IsQueuesView;

    public string SchedulerNavBackground => IsSchedulerView ? "#162033" : "Transparent";
    public string SchedulerNavForeground => IsSchedulerView ? "#F8FAFC" : "#8B98AD";
    public bool SchedulerIndicatorVisible => IsSchedulerView;

    public string SettingsNavBackground => IsSettingsView ? "#162033" : "Transparent";
    public string SettingsNavForeground => IsSettingsView ? "#F8FAFC" : "#8B98AD";
    public bool SettingsIndicatorVisible => IsSettingsView;

    partial void OnCurrentPageChanged(NavigationPage value)
    {
        OnPropertyChanged(nameof(IsDownloadsView));
        OnPropertyChanged(nameof(IsLinkGrabberView));
        OnPropertyChanged(nameof(IsCompletedView));
        OnPropertyChanged(nameof(IsQueuesView));
        OnPropertyChanged(nameof(IsSchedulerView));
        OnPropertyChanged(nameof(IsSettingsView));

        OnPropertyChanged(nameof(DownloadsNavBackground));
        OnPropertyChanged(nameof(DownloadsNavForeground));
        OnPropertyChanged(nameof(DownloadsIndicatorVisible));

        OnPropertyChanged(nameof(LinkGrabberNavBackground));
        OnPropertyChanged(nameof(LinkGrabberNavForeground));
        OnPropertyChanged(nameof(LinkGrabberIndicatorVisible));

        OnPropertyChanged(nameof(CompletedNavBackground));
        OnPropertyChanged(nameof(CompletedNavForeground));
        OnPropertyChanged(nameof(CompletedIndicatorVisible));

        OnPropertyChanged(nameof(QueuesNavBackground));
        OnPropertyChanged(nameof(QueuesNavForeground));
        OnPropertyChanged(nameof(QueuesIndicatorVisible));

        OnPropertyChanged(nameof(SchedulerNavBackground));
        OnPropertyChanged(nameof(SchedulerNavForeground));
        OnPropertyChanged(nameof(SchedulerIndicatorVisible));

        OnPropertyChanged(nameof(SettingsNavBackground));
        OnPropertyChanged(nameof(SettingsNavForeground));
        OnPropertyChanged(nameof(SettingsIndicatorVisible));
    }

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isClipboardMonitorEnabled;

    [ObservableProperty]
    private string _activeTasksText = "0 active  •  0 total";

    [ObservableProperty]
    private string _aggregateSpeedText = "--";

    [ObservableProperty]
    private int _speedLimitValueMb = 50;

    [ObservableProperty]
    private string _statusBarExtraText = string.Empty;

    [ObservableProperty]
    private string _serverPortText = "CNL :9666";

    [ObservableProperty]
    private int _allPackagesCount;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private bool _hasAnyPackages;

    [ObservableProperty]
    private bool _hasCompletedPackages;

    partial void OnSearchFilterChanged(string value)
    {
        foreach (var pkg in Packages)
        {
            pkg.ApplySearchFilter(value);
        }
        UpdateCounters();
    }

    public ObservableCollection<DownloadPackageViewModel> Packages { get; } = new();

    public LinkGrabberViewModel LinkGrabberVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainWindowViewModel(
        IDownloadEngine engine,
        IStateStore stateStore,
        ILinkGrabberService linkGrabber,
        IClickAndLoadServer clickAndLoadServer)
    {
        _engine = engine;
        _stateStore = stateStore;
        _linkGrabber = linkGrabber;
        _clickAndLoadServer = clickAndLoadServer;

        LinkGrabberVm = new LinkGrabberViewModel(_linkGrabber, _engine, () =>
        {
            CurrentPage = NavigationPage.Downloads;
            RefreshPackages();
        });

        SettingsVm = new SettingsViewModel(_engine, _stateStore);

        _engine.ProgressUpdated += OnEngineProgressUpdated;
        _engine.PackageStatusChanged += OnEnginePackageStatusChanged;
        _engine.PackageCompleted += OnEnginePackageCompleted;
        _clickAndLoadServer.LinksReceived += OnClickAndLoadLinksReceived;

        RefreshPackages();
    }

    private void OnClickAndLoadLinksReceived(object? sender, ClickAndLoadLinksEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            CurrentPage = NavigationPage.LinkGrabber;

            if (!string.IsNullOrWhiteSpace(e.PackageName))
            {
                LinkGrabberVm.PreferredPackageName = e.PackageName;
            }

            foreach (var url in e.Urls)
            {
                LinkGrabberVm.AddRawUrl(url);
            }

            if (!string.IsNullOrWhiteSpace(e.Password))
            {
                LinkGrabberVm.ExtractionPassword = e.Password;
            }

            if (!string.IsNullOrWhiteSpace(e.DestinationDirectory))
            {
                LinkGrabberVm.SaveDirectory = e.DestinationDirectory;
            }

            await LinkGrabberVm.AnalyzeLinksAsync();
        });
    }

    public void RefreshPackages()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var currentEnginePackages = _engine.Packages;
            var existingIds = Packages.Select(p => p.Id).ToHashSet();

            // Add new packages
            foreach (var pkg in currentEnginePackages)
            {
                if (!existingIds.Contains(pkg.Id))
                {
                    var vm = new DownloadPackageViewModel(pkg);
                    vm.ApplySearchFilter(SearchFilter);
                    Packages.Add(vm);
                }
            }

            // Remove deleted packages
            var engineIds = currentEnginePackages.Select(p => p.Id).ToHashSet();
            for (int i = Packages.Count - 1; i >= 0; i--)
            {
                if (!engineIds.Contains(Packages[i].Id))
                {
                    Packages.RemoveAt(i);
                }
            }

            // Sync models
            foreach (var pkgVm in Packages)
            {
                pkgVm.SyncFromModel();
                pkgVm.ApplySearchFilter(SearchFilter);
            }

            UpdateStatusText();
        });
    }

    private void OnEngineProgressUpdated(object? sender, EngineProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AggregateSpeedText = FormatHelper.FormatSpeed(e.AggregateSpeedBytesPerSecond);
            ActiveTasksText = $"{e.ActiveTasksCount} active  •  {e.TotalQueueCount} queued";

            foreach (var pkgVm in Packages)
            {
                pkgVm.SyncFromModel();
            }

            UpdateCounters();
        });
    }

    private void OnEnginePackageStatusChanged(object? sender, DownloadPackage package)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var match = Packages.FirstOrDefault(p => p.Id == package.Id);
            if (match != null)
            {
                match.SyncFromModel();
                match.ApplySearchFilter(SearchFilter);
            }
            else
            {
                var vm = new DownloadPackageViewModel(package);
                vm.ApplySearchFilter(SearchFilter);
                Packages.Add(vm);
            }
            UpdateStatusText();
        });
    }

    private void OnEnginePackageCompleted(object? sender, DownloadPackage package)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var match = Packages.FirstOrDefault(p => p.Id == package.Id);
            match?.SyncFromModel();
            match?.ApplySearchFilter(SearchFilter);
            UpdateStatusText();
        });
    }

    private void UpdateStatusText()
    {
        int active = _engine.ActiveTasksCount;
        int total = _engine.Packages.Count;
        ActiveTasksText = $"{active} active  •  {total} total";
        ServerPortText = _clickAndLoadServer.IsRunning ? $"CNL :{_clickAndLoadServer.Port}" : "CNL Offline";
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        AllPackagesCount = Packages.Count;
        ActiveCount = _engine.ActiveTasksCount;
        CompletedCount = Packages.Count(p => p.IsCompleted);
        HasAnyPackages = Packages.Any(p => p.IsVisibleInDownloads);
        HasCompletedPackages = Packages.Any(p => p.IsVisibleInCompleted);
    }

    [RelayCommand]
    public void Navigate(string pageName)
    {
        if (Enum.TryParse<NavigationPage>(pageName, ignoreCase: true, out var page))
        {
            CurrentPage = page;
        }
    }

    [RelayCommand]
    public async Task StartAllAsync()
    {
        await _engine.StartAllAsync();
        RefreshPackages();
    }

    [RelayCommand]
    public async Task PauseAllAsync()
    {
        await _engine.PauseAllAsync();
        RefreshPackages();
    }

    [RelayCommand]
    public async Task StopAllAsync()
    {
        await _engine.PauseAllAsync();
        RefreshPackages();
    }

    [RelayCommand]
    public void AddUrls()
    {
        CurrentPage = NavigationPage.LinkGrabber;
    }

    [RelayCommand]
    public async Task PausePackageAsync(DownloadPackageViewModel? pkgVm)
    {
        if (pkgVm == null) return;
        await _engine.PausePackageAsync(pkgVm.Id);
        pkgVm.SyncFromModel();
    }

    [RelayCommand]
    public async Task ResumePackageAsync(DownloadPackageViewModel? pkgVm)
    {
        if (pkgVm == null) return;
        await _engine.ResumePackageAsync(pkgVm.Id);
        pkgVm.SyncFromModel();
    }

    [RelayCommand]
    public async Task RemovePackageAsync(DownloadPackageViewModel? pkgVm)
    {
        if (pkgVm == null) return;
        await _engine.RemovePackageAsync(pkgVm.Id, deleteFiles: false);
        Packages.Remove(pkgVm);
        UpdateStatusText();
    }

    [RelayCommand]
    public async Task ClearCompletedAsync()
    {
        var completed = Packages.Where(p => p.IsCompleted).ToList();
        foreach (var pkgVm in completed)
        {
            await _engine.RemovePackageAsync(pkgVm.Id, deleteFiles: false);
            Packages.Remove(pkgVm);
        }
        UpdateStatusText();
    }

    [RelayCommand]
    public void OpenPackageFolder(DownloadPackageViewModel? pkgVm)
    {
        if (pkgVm == null) return;
        var dir = pkgVm.DestinationDirectory;
        if (Directory.Exists(dir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    partial void OnSpeedLimitValueMbChanged(int value)
    {
        long bytesPerSec = (long)value * 1024 * 1024;
        _engine.UpdateSpeedLimit(bytesPerSec);
    }
}
