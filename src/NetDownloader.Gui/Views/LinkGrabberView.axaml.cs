using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NetDownloader.Gui.ViewModels;

namespace NetDownloader.Gui.Views;

public partial class LinkGrabberView : UserControl
{
    public LinkGrabberView()
    {
        InitializeComponent();
    }

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Download Directory",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is LinkGrabberViewModel vm)
        {
            vm.SaveDirectory = folders[0].Path.LocalPath;
        }
    }

    private async void OnPasteClipboardClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null || DataContext is not LinkGrabberViewModel vm) return;

        try
        {
            var text = await topLevel.Clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                vm.AddRawUrl(text.Trim());
            }
        }
        catch
        {
            // Ignore clipboard access error
        }
    }
}
