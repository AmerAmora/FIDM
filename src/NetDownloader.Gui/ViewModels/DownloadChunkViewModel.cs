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
    private string _fillColor = "#182030";

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
            FillColor = "#10B981"; // Vibrant Emerald
        }
        else if (IsCompleted || ProgressPercentage >= 100.0)
        {
            FillColor = "#0EA5E9"; // Electric Sky Cyan
        }
        else if (ProgressPercentage > 0)
        {
            FillColor = "#3B82F6"; // Royal Azure
        }
        else
        {
            FillColor = "#182030"; // Recessed Dark Slate
        }
    }
}
