using Avalonia.Controls;
using Avalonia.Threading;
using NetDownloader.Gui.ViewModels;

namespace NetDownloader.Gui;

public partial class MainWindow : Window
{
    private IDisposable? _clipboardTimer;
    private string? _lastClipboardText;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _clipboardTimer = DispatcherTimer.Run(() =>
        {
            if (DataContext is MainWindowViewModel { IsClipboardMonitorEnabled: true } vm && Clipboard != null)
            {
                CheckClipboardAsync(vm);
            }
            return true;
        }, TimeSpan.FromSeconds(1.5));
    }

    private async void CheckClipboardAsync(MainWindowViewModel vm)
    {
        try
        {
            var text = await Clipboard!.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboardText)
            {
                _lastClipboardText = text;
                if (text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("https://", StringComparison.OrdinalIgnoreCase))
                {
                    vm.LinkGrabberVm.AddRawUrl(text);
                }
            }
        }
        catch
        {
            // Ignore transient clipboard access errors
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clipboardTimer?.Dispose();
        base.OnClosed(e);
    }
}
