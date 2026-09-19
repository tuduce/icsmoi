using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using icsmoi.ViewModels;
using NodifyM.Avalonia.Controls;

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

            // Opened by the Runtime's Edit button: load that profile (errors show in the status field).
            if (vm.StartupProfilePath is { } path)
                _ = vm.LoadFromPathAsync(path);
        }
    }

    // ── Pin anchors after a node's pins change ──────────────────────────────
    // NodifyM only recomputes a connector's anchor (where its wire attaches) when the node moves or
    // its size changes. Removing a Joystick Input row moves the pins below it up; if the node's size
    // happens not to change (its body is taller than the pin column) their wires would keep pointing
    // at the old spots. So after such an edit, once layout has settled, re-measure every connector.

    private MainWindowViewModel? _subscribedViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null) _subscribedViewModel.PinLayoutChanged -= OnPinLayoutChanged;
        _subscribedViewModel = DataContext as MainWindowViewModel;
        if (_subscribedViewModel is not null) _subscribedViewModel.PinLayoutChanged += OnPinLayoutChanged;
    }

    private void OnPinLayoutChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var connector in Editor.GetVisualDescendants().OfType<Connector>())
                connector.UpdateAnchor();
        }, DispatcherPriority.Background); // below Layout/Render, so positions are final
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

    // Save: straight over the profile's own file once it has one (so a profile opened from the Runtime
    // is saved where the Runtime will pick it up); the first save of a new profile is a Save As.
    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (!await vm.TrySaveInPlaceAsync())
            await SaveAsAsync(vm);
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await SaveAsAsync(vm);
    }

    private async System.Threading.Tasks.Task SaveAsAsync(MainWindowViewModel vm)
    {
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