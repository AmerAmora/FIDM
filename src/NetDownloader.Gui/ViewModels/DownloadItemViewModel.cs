using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDownloader.Core.Models;
using NetDownloader.Gui.ViewModels.Helpers;

namespace NetDownloader.Gui.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadItem _model;
    private long _lastDownloadedBytes;
    private long _lastSampleTimestamp;
    private long _currentSpeed;

    public DownloadItem Model => _model;

    public Guid Id => _model.Id;
    public string Url => _model.Url;
    public long CurrentSpeed => _model.Status == DownloadItemStatus.Downloading ? _currentSpeed : 0;

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
    private string _badgeBackground = "#1F64748B";

    [ObservableProperty]
    private string _badgeBorder = "#4064748B";

    [ObservableProperty]
    private string _badgeForeground = "#94A3B8";

    [ObservableProperty]
    private string _progressSubtitle = "--";

    [ObservableProperty]
    private string _progressBarColor = "#3B82F6";

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
        void Apply()
        {
            FileName = _model.FileName;
            FormattedSize = FormatHelper.FormatBytes(_model.TotalBytes);
            FormattedDownloaded = FormatHelper.FormatBytes(_model.DownloadedBytes);
            ProgressPercentage = _model.ProgressPercentage;

            RefreshSpeedAndEtaOnUiThread();
            UpdateStatusPresentation();
            UpdateVisualChunks();

            if (IsCompleted)
            {
                ProgressSubtitle = _model.TotalBytes > 0 ? $"{FormattedSize}  •  100%" : "Completed";
            }
            else if (_model.TotalBytes > 0 && _model.DownloadedBytes > 0)
            {
                ProgressSubtitle = $"{FormattedDownloaded} / {FormattedSize}  •  {ProgressPercentage:0.#}%";
            }
            else if (_model.DownloadedBytes > 0)
            {
                ProgressSubtitle = $"{FormattedDownloaded} downloaded";
            }
            else
            {
                ProgressSubtitle = FormattedSize;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void RefreshSpeedAndEtaOnUiThread()
    {
        if (_model.Status == DownloadItemStatus.Downloading)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastSampleTimestamp == 0)
            {
                _lastSampleTimestamp = now;
                _lastDownloadedBytes = _model.DownloadedBytes;
            }
            else
            {
                double elapsedSeconds = (double)(now - _lastSampleTimestamp) / Stopwatch.Frequency;
                if (elapsedSeconds >= 0.2)
                {
                    long deltaBytes = Math.Max(0, _model.DownloadedBytes - _lastDownloadedBytes);
                    long instantSpeed = (long)(deltaBytes / elapsedSeconds);
                    _currentSpeed = _currentSpeed == 0 ? instantSpeed : (long)(0.7 * _currentSpeed + 0.3 * instantSpeed);

                    _lastDownloadedBytes = _model.DownloadedBytes;
                    _lastSampleTimestamp = now;
                }
            }

            SpeedString = FormatHelper.FormatSpeed(_currentSpeed);
            if (_model.TotalBytes > 0)
            {
                long remaining = Math.Max(0, _model.TotalBytes - _model.DownloadedBytes);
                EtaString = FormatHelper.FormatEta(remaining, _currentSpeed);
            }
            else
            {
                EtaString = "--";
            }
        }
        else if (_model.Status == DownloadItemStatus.Extracting)
        {
            _currentSpeed = 0;
            _lastSampleTimestamp = 0;
            SpeedString = "--";
            EtaString = "Extracting...";
        }
        else
        {
            _currentSpeed = 0;
            _lastSampleTimestamp = 0;
            _lastDownloadedBytes = _model.DownloadedBytes;
            SpeedString = "--";
            EtaString = "--";
        }
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

                if (_model.TotalBytes > 0)
                {
                    long remaining = Math.Max(0, _model.TotalBytes - _model.DownloadedBytes);
                    EtaString = FormatHelper.FormatEta(remaining, _currentSpeed);
                }
                else
                {
                    EtaString = "--";
                }
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
                BadgeBackground = "#1F3B82F6";
                BadgeBorder = "#4D3B82F6";
                BadgeForeground = "#60A5FA";
                ProgressBarColor = "#3B82F6";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Extracting:
                StatusText = "Extracting";
                BadgeBackground = "#1FF59E0B";
                BadgeBorder = "#4DF59E0B";
                BadgeForeground = "#FBBF24";
                ProgressBarColor = "#F59E0B";
                IsExtracting = true;
                break;
            case DownloadItemStatus.Completed:
                StatusText = "Completed";
                BadgeBackground = "#1F10B981";
                BadgeBorder = "#4D10B981";
                BadgeForeground = "#34D399";
                ProgressBarColor = "#10B981";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Online:
                StatusText = "Online";
                BadgeBackground = "#1F10B981";
                BadgeBorder = "#4D10B981";
                BadgeForeground = "#34D399";
                ProgressBarColor = "#10B981";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Offline:
                StatusText = "Offline";
                BadgeBackground = "#1FEF4444";
                BadgeBorder = "#4DEF4444";
                BadgeForeground = "#F87171";
                ProgressBarColor = "#EF4444";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Paused:
                StatusText = "Paused";
                BadgeBackground = "#1FF59E0B";
                BadgeBorder = "#4DF59E0B";
                BadgeForeground = "#FBBF24";
                ProgressBarColor = "#F59E0B";
                IsExtracting = false;
                break;
            case DownloadItemStatus.Failed:
                StatusText = "Failed";
                BadgeBackground = "#1FEF4444";
                BadgeBorder = "#4DEF4444";
                BadgeForeground = "#F87171";
                ProgressBarColor = "#EF4444";
                IsExtracting = false;
                break;
            default:
                StatusText = "Queued";
                BadgeBackground = "#1F64748B";
                BadgeBorder = "#4064748B";
                BadgeForeground = "#94A3B8";
                ProgressBarColor = "#3B82F6";
                IsExtracting = false;
                break;
        }
    }
}
