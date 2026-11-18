using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDownloader.Core.Models;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public partial class DownloadPackageViewModel : ObservableObject
{
    private readonly DownloadPackage _model;
    private string _currentFilter = string.Empty;

    public DownloadPackage Model => _model;

    public Guid Id => _model.Id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _subtitleText = "Package";

    [ObservableProperty]
    private string _destinationDirectory = string.Empty;

    [ObservableProperty]
    private string? _extractionPassword;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private string _formattedTotalSize = "--";

    [ObservableProperty]
    private string _formattedDownloaded = "0 B";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _speedString = "--";

    [ObservableProperty]
    private string _etaString = "--";

    [ObservableProperty]
    private string _statusText = "Active";

    [ObservableProperty]
    private string _badgeBackground = "#1F64748B";

    [ObservableProperty]
    private string _badgeBorder = "#4064748B";

    [ObservableProperty]
    private string _badgeForeground = "#94A3B8";

    [ObservableProperty]
    private bool _isBadgeVisible = true;

    [ObservableProperty]
    private string _progressBarColor = "#3B82F6";

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _matchesFilter = true;

    [ObservableProperty]
    private bool _isVisibleInDownloads = true;

    [ObservableProperty]
    private bool _isVisibleInCompleted;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public DownloadPackageViewModel(DownloadPackage model)
    {
        _model = model;
        _name = model.Name;
        _destinationDirectory = model.DestinationDirectory;
        _extractionPassword = model.ExtractionPassword;

        foreach (var item in model.Items)
        {
            Items.Add(new DownloadItemViewModel(item));
        }

        SyncFromModel();
    }

    public void ApplySearchFilter(string filter)
    {
        _currentFilter = filter?.Trim() ?? string.Empty;
        UpdateVisibilityFlags();
    }

    private void UpdateVisibilityFlags()
    {
        if (string.IsNullOrWhiteSpace(_currentFilter))
        {
            MatchesFilter = true;
        }
        else
        {
            MatchesFilter = Name.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) ||
                            Items.Any(i => i.FileName.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase));
        }

        IsVisibleInDownloads = MatchesFilter;
        IsVisibleInCompleted = MatchesFilter && IsCompleted;
    }

    public void SyncFromModel()
    {
        void Apply()
        {
            Name = _model.Name;
            DestinationDirectory = _model.DestinationDirectory;
            ExtractionPassword = _model.ExtractionPassword;

            bool allUnknown = _model.Items.Count > 0 && _model.Items.All(i => i.TotalBytes <= 0);
            FormattedTotalSize = allUnknown ? "--" : FormatHelper.FormatBytes(_model.TotalBytes);
            FormattedDownloaded = FormatHelper.FormatBytes(_model.DownloadedBytes);
            ProgressPercentage = _model.ProgressPercentage;

            // Sync missing child items first so status & telemetry inspect fresh item state
            var existingIds = Items.Select(i => i.Id).ToHashSet();
            foreach (var item in _model.Items)
            {
                if (!existingIds.Contains(item.Id))
                {
                    Items.Add(new DownloadItemViewModel(item));
                }
            }

            foreach (var itemVm in Items)
            {
                itemVm.SyncFromModel();
            }

            UpdateStatusPresentation();
            UpdateVisibilityFlags();

            int partCount = _model.Items.Count;
            string partWord = partCount == 1 ? "1 file" : $"{partCount} files";
            if (IsCompleted)
            {
                SubtitleText = allUnknown ? $"{partWord}  •  Completed" : $"{partWord}  •  {FormattedTotalSize}  •  100%";
            }
            else if (_model.DownloadedBytes > 0 && _model.TotalBytes > 0)
            {
                SubtitleText = $"{partWord}  •  {FormattedDownloaded} / {FormattedTotalSize}  ({ProgressPercentage:0.#}%)";
            }
            else if (!string.IsNullOrWhiteSpace(DestinationDirectory))
            {
                SubtitleText = $"{partWord}  •  {DestinationDirectory}";
            }
            else
            {
                SubtitleText = partWord;
            }

            if (_model.Status == PackageStatus.Downloading)
            {
                long pkgSpeed = Items.Sum(i => i.CurrentSpeed);
                SpeedString = FormatHelper.FormatSpeed(pkgSpeed);
                if (_model.TotalBytes > 0)
                {
                    long remaining = Math.Max(0, _model.TotalBytes - _model.DownloadedBytes);
                    EtaString = FormatHelper.FormatEta(remaining, pkgSpeed);
                }
                else
                {
                    EtaString = "--";
                }
            }
            else if (_model.Status == PackageStatus.Extracting)
            {
                SpeedString = "--";
                EtaString = "Extracting...";
            }
            else
            {
                SpeedString = "--";
                EtaString = "--";
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void UpdateStatusPresentation()
    {
        IsCompleted = _model.Status == PackageStatus.Completed || _model.Status == PackageStatus.Extracted;
        IsDownloading = _model.Status == PackageStatus.Downloading;
        CanResume = !IsCompleted && !IsDownloading && _model.Status != PackageStatus.Extracting;

        switch (_model.Status)
        {
            case PackageStatus.Downloading:
                StatusText = "Downloading";
                BadgeBackground = "#1F3B82F6";
                BadgeBorder = "#4D3B82F6";
                BadgeForeground = "#60A5FA";
                ProgressBarColor = "#3B82F6";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Extracting:
                StatusText = "Extracting";
                BadgeBackground = "#1FF59E0B";
                BadgeBorder = "#4DF59E0B";
                BadgeForeground = "#FBBF24";
                ProgressBarColor = "#F59E0B";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Extracted:
            case PackageStatus.Completed:
                StatusText = _model.Status == PackageStatus.Extracted ? "Extracted" : "Completed";
                BadgeBackground = "#1F10B981";
                BadgeBorder = "#4D10B981";
                BadgeForeground = "#34D399";
                ProgressBarColor = "#10B981";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Paused:
                StatusText = "Paused";
                BadgeBackground = "#1FF59E0B";
                BadgeBorder = "#4DF59E0B";
                BadgeForeground = "#FBBF24";
                ProgressBarColor = "#F59E0B";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Failed:
                StatusText = "Failed";
                BadgeBackground = "#1FEF4444";
                BadgeBorder = "#4DEF4444";
                BadgeForeground = "#F87171";
                ProgressBarColor = "#EF4444";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Queued:
            default:
                if (Items.Count > 0 && Items.All(i => i.StatusText == "Online"))
                {
                    StatusText = "Online";
                    BadgeBackground = "#1F10B981";
                    BadgeBorder = "#4D10B981";
                    BadgeForeground = "#34D399";
                    ProgressBarColor = "#10B981";
                    IsBadgeVisible = true;
                }
                else
                {
                    StatusText = "Queued";
                    BadgeBackground = "#1F64748B";
                    BadgeBorder = "#4064748B";
                    BadgeForeground = "#94A3B8";
                    ProgressBarColor = "#3B82F6";
                    IsBadgeVisible = true;
                }
                break;
        }
    }
}
