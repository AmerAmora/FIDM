using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDownloader.Core.Engine;
using NetDownloader.Core.Models;
using NetDownloader.Core.Services;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public partial class LinkGrabberViewModel : ObservableObject
{
    private readonly ILinkGrabberService _linkGrabber;
    private readonly IDownloadEngine _engine;
    private readonly Action _onEnqueuedNavigation;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _hasDetectedPackages;

    [ObservableProperty]
    private string _analyzeButtonText = "Analyze Links";

    [ObservableProperty]
    private string _saveDirectory = string.Empty;

    [ObservableProperty]
    private string _extractionPassword = string.Empty;

    [ObservableProperty]
    private string _summaryText = "0 packages • 0 B total";

    [ObservableProperty]
    private string? _preferredPackageName;

    public ObservableCollection<DownloadPackageViewModel> DetectedPackages { get; } = new();

    public LinkGrabberViewModel(
        ILinkGrabberService linkGrabber,
        IDownloadEngine engine,
        Action onEnqueuedNavigation)
    {
        _linkGrabber = linkGrabber;
        _engine = engine;
        _onEnqueuedNavigation = onEnqueuedNavigation;
        _saveDirectory = _engine.Configuration.DefaultDownloadDirectory;
    }

    [RelayCommand]
    public async Task AnalyzeLinksAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsAnalyzing)
            return;

        IsAnalyzing = true;
        AnalyzeButtonText = "Analyzing...";

        try
        {
            var packages = await _linkGrabber.AnalyzeAsync(InputText, SaveDirectory);

            if (!string.IsNullOrWhiteSpace(PreferredPackageName))
            {
                foreach (var pkg in packages)
                {
                    if (LinkGrabberService.IsGenericOrTokenFileName(pkg.Name))
                    {
                        pkg.Name = PreferredPackageName;
                    }
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                DetectedPackages.Clear();
                long totalBytes = 0;
                foreach (var pkg in packages)
                {
                    totalBytes += pkg.TotalBytes;
                    DetectedPackages.Add(new DownloadPackageViewModel(pkg));
                }

                var sizeText = totalBytes > 0
                    ? $"{FormatHelper.FormatBytes(totalBytes)} total"
                    : "Unknown size";
                HasDetectedPackages = DetectedPackages.Count > 0;
                SummaryText = HasDetectedPackages
                    ? $"{DetectedPackages.Count} package(s)  •  {sizeText}"
                    : "0 packages  •  0 B total";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SummaryText = $"Analysis error: {ex.Message}";
            });
        }
        finally
        {
            IsAnalyzing = false;
            AnalyzeButtonText = "Analyze Links";
        }
    }

    [RelayCommand]
    public void ClearInput()
    {
        InputText = string.Empty;
        PreferredPackageName = null;
        DetectedPackages.Clear();
        HasDetectedPackages = false;
        SummaryText = "0 packages  •  0 B total";
    }

    [RelayCommand]
    public async Task EnqueueAndStartDownloadsAsync()
    {
        if (DetectedPackages.Count == 0)
            return;

        var packagesToEnqueue = new List<DownloadPackage>();

        foreach (var vm in DetectedPackages)
        {
            var pkg = vm.Model;
            pkg.DestinationDirectory = SaveDirectory;
            if (!string.IsNullOrWhiteSpace(ExtractionPassword))
            {
                pkg.ExtractionPassword = ExtractionPassword;
            }
            pkg.Status = PackageStatus.Queued;
            foreach (var item in pkg.Items)
            {
                item.Status = DownloadItemStatus.Queued;
            }
            packagesToEnqueue.Add(pkg);
        }

        await _engine.EnqueuePackagesAsync(packagesToEnqueue);
        await _engine.StartAllAsync();

        Dispatcher.UIThread.Post(() =>
        {
            DetectedPackages.Clear();
            HasDetectedPackages = false;
            InputText = string.Empty;
            SummaryText = "0 packages  •  0 B total";
            _onEnqueuedNavigation();
        });
    }

    public void AddRawUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        InputText = string.IsNullOrWhiteSpace(InputText) ? url : $"{InputText}{Environment.NewLine}{url}";
    }
}
