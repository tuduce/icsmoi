using Avalonia.Controls;
using Avalonia.Input;
using icsmooi.ViewModels;

namespace icsmooi.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
                vm.Engine.SimData["AIRSPEED INDICATED"] = 250;
                vm.DeleteSelectionCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}