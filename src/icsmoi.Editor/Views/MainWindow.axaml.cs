using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using icsmoi.ViewModels;

namespace icsmoi.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // SimConnect's constructor requires a real native window handle even though
    // this app never pumps Win32 messages for it (see SimConnectTelemetryService).
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel vm)
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            vm.AttachWindowHandle(handle);
        }
    }

    // ── Profile name editing ────────────────────────────────────────────────

    private void OnEditProfileNameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.BeginEditProfileNameCommand.Execute(null);
        // The TextBox only becomes visible once the binding above has propagated,
        // so focus it after layout rather than right now.
        Dispatcher.UIThread.Post(() =>
        {
            ProfileNameBox.Focus();
            ProfileNameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnProfileNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            vm.CommitProfileNameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelEditProfileNameCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Clicking anywhere else commits (the command ignores this if Enter/Esc already closed the editor).
    private void OnProfileNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CommitProfileNameCommand.Execute(null);
    }

    // ── Profile open/save via the OS file pickers ───────────────────────────

    private static readonly FilePickerFileType ProfileFileType =
        new("icsmoi profile") { Patterns = ["*" + MainWindowViewModel.ProfileFileExtension] };

    // Reopen where the user last was; fall back to Documents the first time.
    private async System.Threading.Tasks.Task<IStorageFolder?> GetStartLocationAsync(MainWindowViewModel vm)
    {
        var dir = vm.CurrentFilePath is { } current ? Path.GetDirectoryName(current) : null;
        if (dir is not null && await StorageProvider.TryGetFolderFromPathAsync(dir) is { } folder)
            return folder;
        return await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save icsmoi profile",
            SuggestedFileName = vm.SuggestedFileName,
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await GetStartLocationAsync(vm),
            FileTypeChoices = [ProfileFileType],
        });

        if (file?.TryGetLocalPath() is { } path)
            await vm.SaveToPathAsync(path);
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open icsmoi profile",
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartLocationAsync(vm),
            FileTypeFilter = [ProfileFileType, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await vm.LoadFromPathAsync(path);
    }

    // Route Delete to the node-deletion command whenever focus is NOT inside a
    // text input.  This covers two cases the NodifyEditor's own KeyBinding misses:
    //  • Focus is on a palette Button (different visual branch — event never
    //    travels through NodifyEditor during bubbling).
    //  • Focus is on a TextBox inside a node (TextBox marks the event as handled
    //    before it reaches NodifyEditor's KeyBinding check).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && e.Key == Key.Delete && e.Source is not TextBox)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DeleteSelectionCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}