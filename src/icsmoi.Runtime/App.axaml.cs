using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using icsmoi.Runtime.Views;

namespace icsmoi.Runtime;

public partial class App : Application
{
    private Window? _statusWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _statusWindow = new StatusWindow();
            desktop.MainWindow = _statusWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShowStatusClicked(object? sender, System.EventArgs e)
    {
        if (_statusWindow is null) return;
        _statusWindow.Show();
        _statusWindow.WindowState = WindowState.Normal;
        _statusWindow.Activate();
    }

    private void OnExitClicked(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
