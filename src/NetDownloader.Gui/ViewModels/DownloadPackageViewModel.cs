using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDownloader.Core.Models;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public partial class DownloadPackageViewModel : ObservableObject
{
    private readonly DownloadPackage _model;

    public DownloadPackage Model => _model;

    public Guid Id => _model.Id;

    [ObservableProperty]
    private string _name = string.Empty;

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
    private string _statusText = "Active";

    [ObservableProperty]
    private string _badgeBackground = "Transparent";

    [ObservableProperty]
    private string _badgeForeground = "#9CA3AF";

    [ObservableProperty]
    private bool _isBadgeVisible;

    [ObservableProperty]
    private string _progressBarColor = "#2D8CFF";

    [ObservableProperty]
    private bool _isCompleted;

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

    public void SyncFromModel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Name = _model.Name;
            DestinationDirectory = _model.DestinationDirectory;
            ExtractionPassword = _model.ExtractionPassword;
            FormattedTotalSize = FormatHelper.FormatBytes(_model.TotalBytes);
            FormattedDownloaded = FormatHelper.FormatBytes(_model.DownloadedBytes);
            ProgressPercentage = _model.ProgressPercentage;

            UpdateStatusPresentation();

            // Sync missing child items
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
        });
    }

    private void UpdateStatusPresentation()
    {
        IsCompleted = _model.Status == PackageStatus.Completed || _model.Status == PackageStatus.Extracted;

        switch (_model.Status)
        {
            case PackageStatus.Downloading:
                StatusText = "Active";
                BadgeBackground = "Transparent";
                BadgeForeground = "#60A5FA"; // light blue
                ProgressBarColor = "#2D8CFF";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Extracting:
                StatusText = "Extracting";
                BadgeBackground = "#D97706";
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#D97706";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Extracted:
            case PackageStatus.Completed:
                StatusText = "Completed";
                BadgeBackground = "#10B981";
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#10B981";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Paused:
                StatusText = "Paused";
                BadgeBackground = "#4B5563";
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#4B5563";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Failed:
                StatusText = "Failed";
                BadgeBackground = "#DC2626";
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#DC2626";
                IsBadgeVisible = true;
                break;
            case PackageStatus.Queued:
            default:
                if (Items.All(i => i.StatusText == "Online"))
                {
                    StatusText = "Online";
                    BadgeBackground = "#059669";
                    BadgeForeground = "#FFFFFF";
                    ProgressBarColor = "#059669";
                    IsBadgeVisible = true;
                }
                else
                {
                    StatusText = "Queued";
                    BadgeBackground = "Transparent";
                    BadgeForeground = "#9CA3AF";
                    ProgressBarColor = "#2D8CFF";
                    IsBadgeVisible = true;
                }
                break;
        }
    }
}
