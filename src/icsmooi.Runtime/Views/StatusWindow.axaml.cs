using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using icsmooi.Runtime.ViewModels;

namespace icsmooi.Runtime.Views;

public partial class StatusWindow : Window
{
    private readonly StatusWindowViewModel _viewModel = new();

    public StatusWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    // SimConnect and DirectInput both need a real native window handle.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _viewModel.AttachWindowHandle(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private async void OnLoadProfileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load icsmooi profile",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
            FileTypeFilter = [new FilePickerFileType("icsmooi profile") { Patterns = ["*.icsmooi.json"] }],
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file?.TryGetLocalPath() is { } path)
            await _viewModel.LoadProfileCommand.ExecuteAsync(path);
    }
}
