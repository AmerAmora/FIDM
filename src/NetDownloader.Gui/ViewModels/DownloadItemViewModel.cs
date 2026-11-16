using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDownloader.Core.Models;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadItem _model;
    private long _lastDownloadedBytes;
    private long _currentSpeed;

    public DownloadItem Model => _model;

    public Guid Id => _model.Id;
    public string Url => _model.Url;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _formattedSize = "--";

    [ObservableProperty]
    private string _formattedDownloaded = "0 B";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _speedString = "--";

    [ObservableProperty]
    private string _etaString = "--";

    [ObservableProperty]
    private string _statusText = "Queued";

    [ObservableProperty]
    private string _badgeBackground = "#374151";

    [ObservableProperty]
    private string _badgeForeground = "#FFFFFF";

    [ObservableProperty]
    private string _progressBarColor = "#2D8CFF";

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isExtracting;

    public ObservableCollection<DownloadChunkViewModel> Chunks { get; } = new();

    public DownloadItemViewModel(DownloadItem model)
    {
        _model = model;
        _fileName = model.FileName;
        _formattedSize = FormatHelper.FormatBytes(model.TotalBytes);
        _lastDownloadedBytes = model.DownloadedBytes;

        InitializeVisualChunks();
        SyncFromModel();
    }

    private void InitializeVisualChunks()
    {
        Chunks.Clear();
        const int visualSegments = 16;
        for (int i = 0; i < visualSegments; i++)
        {
            Chunks.Add(new DownloadChunkViewModel(i));
        }
    }

    public void SyncFromModel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            FileName = _model.FileName;
            FormattedSize = FormatHelper.FormatBytes(_model.TotalBytes);
            FormattedDownloaded = FormatHelper.FormatBytes(_model.DownloadedBytes);
            ProgressPercentage = _model.ProgressPercentage;

            UpdateStatusPresentation();
            UpdateVisualChunks();
        });
    }

    public void UpdateSpeedAndEta(long bytesTransferredInInterval, double intervalSeconds)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_model.Status == DownloadItemStatus.Downloading && intervalSeconds > 0)
            {
                long instantSpeed = (long)(bytesTransferredInInterval / intervalSeconds);
                _currentSpeed = _currentSpeed == 0 ? instantSpeed : (long)(0.7 * _currentSpeed + 0.3 * instantSpeed);

                SpeedString = FormatHelper.FormatSpeed(_currentSpeed);

                long remaining = Math.Max(0, _model.TotalBytes - _model.DownloadedBytes);
                EtaString = FormatHelper.FormatEta(remaining, _currentSpeed);
            }
            else if (_model.Status == DownloadItemStatus.Extracting)
            {
                SpeedString = "--";
                EtaString = "Extracting files...";
            }
            else
            {
                SpeedString = "--";
                EtaString = "--";
            }
        });
    }

    private void UpdateVisualChunks()
    {
        bool isItemDone = _model.Status == DownloadItemStatus.Completed;
        const int visualCount = 16;
        if (_model.Chunks.Count == 0 || isItemDone)
        {
            double overallPct = _model.ProgressPercentage;
            int filledCount = isItemDone ? visualCount : (int)Math.Round((overallPct / 100.0) * visualCount);
            for (int i = 0; i < Chunks.Count; i++)
            {
                bool isCompleted = i < filledCount;
                Chunks[i].UpdateProgress(isCompleted ? 100.0 : 0.0, isCompleted, isItemDone);
            }
        }
        else
        {
            // Map actual chunks to visual slots
            for (int i = 0; i < Chunks.Count; i++)
            {
                double slotRatio = (double)i / visualCount;
                long estimatedOffset = _model.TotalBytes > 0 ? (long)(slotRatio * _model.TotalBytes) : 0;

                var matchingChunk = _model.Chunks.FirstOrDefault(c =>
                    c.StartByte <= estimatedOffset && (c.EndByte >= estimatedOffset || c.EndByte <= 0));

                if (matchingChunk != null)
                {
                    Chunks[i].UpdateProgress(matchingChunk.ProgressPercentage, matchingChunk.IsCompleted, false);
                }
                else
                {
                    bool done = _model.ProgressPercentage >= ((double)(i + 1) / visualCount * 100.0);
                    Chunks[i].UpdateProgress(done ? 100.0 : 0.0, done, false);
                }
            }
        }
    }

    private void UpdateStatusPresentation()
    {
        IsCompleted = _model.Status == DownloadItemStatus.Completed;

        switch (_model.Status)
        {
            case DownloadItemStatus.Downloading:
                StatusText = "Downloading";
                BadgeBackground = "#2563EB"; // Blue
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#2D8CFF";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Extracting:
                StatusText = "Extracting";
                BadgeBackground = "#D97706"; // Amber
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#D97706";
                IsExtracting = true;
                break;
            case DownloadItemStatus.Completed:
                StatusText = "Completed";
                BadgeBackground = "#10B981"; // Emerald
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#10B981";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Online:
                StatusText = "Online";
                BadgeBackground = "#059669"; // Green
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#059669";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Offline:
                StatusText = "Offline";
                BadgeBackground = "#6B7280"; // Gray
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#6B7280";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Paused:
                StatusText = "Paused";
                BadgeBackground = "#4B5563"; // Gray
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#4B5563";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Failed:
                StatusText = "Failed";
                BadgeBackground = "#DC2626"; // Red
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#DC2626";
                IsExtracting = false;
                break;
            default:
                StatusText = "Queued";
                BadgeBackground = "#374151";
                BadgeForeground = "#FFFFFF";
                ProgressBarColor = "#2D8CFF";
                IsExtracting = false;
                break;
        }
    }
}
