using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmooi.Models;

/// <summary>
/// A real DirectInput Ramp Force effect (linearly ramps from StartMagnitude to
/// EndMagnitude over Duration). No TDX-Air-Mechanics analog — added for
/// completeness (e.g. a touchdown thump or gear-deploy force build-up).
/// </summary>
public partial class RampForceOutputNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private Guid? _deviceInstanceGuid;

    [ObservableProperty]
    private int _axisIndex;

    [ObservableProperty]
    private int _gain = 10000;

    [JsonIgnore]
    [ObservableProperty]
    private double _lastStartMagnitude;

    public RampForceOutputNodeViewModel()
    {
        Name = "Ramp Force";
        Inputs.Add(new PinViewModel { Title = "StartMagnitude", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "EndMagnitude", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Duration", IsInput = true });
    }
}
