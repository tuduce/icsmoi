using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace icsmoi.Models;

/// <summary>
/// A real DirectInput Constant Force effect output. This node is a genuine
/// hardware target: <c>Services.DirectInput.FfbEffectManager</c> creates one
/// long-lived DirectInput effect per node instance and keeps it updated in
/// place every tick — never recreated per tick.
/// </summary>
public partial class ConstantForceOutputNodeViewModel : HardwareOutputNodeViewModel
{
    /// <summary>Last magnitude actually sent to hardware — runtime-only, never persisted.</summary>
    [JsonIgnore]
    [ObservableProperty]
    private double _lastMagnitude;

    public ConstantForceOutputNodeViewModel()
    {
        Name = "Constant Force";
        Inputs.Add(new PinViewModel { Title = "Magnitude", IsInput = true });
    }
}
