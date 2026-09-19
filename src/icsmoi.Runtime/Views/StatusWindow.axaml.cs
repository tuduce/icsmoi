using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using icsmoi.Runtime.ViewModels;

namespace icsmoi.Runtime.Views;

public partial class StatusWindow : Window
{
    private readonly StatusWindowViewModel _viewModel = new();

    public StatusWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.LoadRequested += OnLoadRequested;
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
        _viewModel.LoadRequested -= OnLoadRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    // Clicking anywhere on a slot card runs its profile — except on the card's own buttons.
    private void OnSlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ProfileSlotViewModel slot }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;

        slot.SelectCommand.Execute(null);
    }

    private async void OnLoadRequested(ProfileSlotViewModel slot)
    {
        // Start where the profiles already are; Documents the first time.
        var existing = _viewModel.Slots.FirstOrDefault(s => s.FilePath is not null)?.FilePath;
        var directory = slot.FilePath is { } own ? Path.GetDirectoryName(own) : existing is null ? null : Path.GetDirectoryName(existing);
        var start = directory is not null && await StorageProvider.TryGetFolderFromPathAsync(directory) is { } folder
            ? folder
            : await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Load icsmoi profile into slot {slot.Index + 1}",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("icsmoi profile") { Patterns = ["*.icsmoi.json"] }],
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file?.TryGetLocalPath() is { } path)
            await _viewModel.LoadIntoSlotAsync(slot, path);
    }
}
