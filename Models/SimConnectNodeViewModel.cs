using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

public partial class SimConnectNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private string _variableName = "AIRSPEED INDICATED";

    public SimConnectNodeViewModel()
    {
        Name = "SimConnect Input";
        Outputs.Add(new PinViewModel { Name = "Value" });
    }
}
