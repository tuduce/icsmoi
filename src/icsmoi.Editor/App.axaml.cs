using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using icsmoi.ViewModels;
using icsmoi.Views;

namespace icsmoi;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel { StartupProfilePath = ParseProfileArgument(desktop.Args) },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The Runtime opens this app on a specific profile as `icsmoi.Editor.exe --profile "<path>"`.
    private static string? ParseProfileArgument(string[]? args)
    {
        if (args is null) return null;
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--profile", System.StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}