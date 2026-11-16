using CommunityToolkit.Mvvm.ComponentModel;

namespace NetDownloader.Gui.ViewModels;

public partial class DownloadChunkViewModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _fillColor = "#1E2533";

    public DownloadChunkViewModel(int index, double progressPercentage = 0, bool isCompleted = false)
    {
        _index = index;
        _progressPercentage = progressPercentage;
        _isCompleted = isCompleted;
        UpdateColor(false);
    }

    public void UpdateProgress(double percentage, bool isCompleted, bool isItemCompleted = false)
    {
        ProgressPercentage = percentage;
        IsCompleted = isCompleted;
        UpdateColor(isItemCompleted);
    }

    private void UpdateColor(bool isItemCompleted = false)
    {
        if (isItemCompleted)
        {
            FillColor = "#10B981"; // Emerald Green for completed file
        }
        else if (IsCompleted || ProgressPercentage >= 100.0)
        {
            FillColor = "#2D8CFF"; // Vibrant Cyan-Blue
        }
        else if (ProgressPercentage > 0)
        {
            FillColor = "#60A5FA"; // Lighter In-Progress Blue
        }
        else
        {
            FillColor = "#1E2533"; // Dark Background
        }
    }
}
