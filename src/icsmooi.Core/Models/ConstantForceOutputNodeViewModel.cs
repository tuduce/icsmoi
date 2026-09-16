using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace icsmooi.Models;

/// <summary>
/// A real DirectInput Constant Force effect output. Unlike the placeholder
/// <see cref="FfbOutputNodeViewModel"/>, this node is a genuine hardware
/// target: <c>Services.DirectInput.FfbEffectManager</c> creates one
/// long-lived DirectInput effect per node instance and keeps it updated in
/// place every tick — never recreated per tick.
/// </summary>
public partial class ConstantForceOutputNodeViewModel : NodeViewModel
{
    /// <summary>Which physical FFB device this effect targets — null until the user picks one.</summary>
    [ObservableProperty]
    private Guid? _deviceInstanceGuid;

    /// <summary>Index into the target device's cached FFB axis offsets (see <c>FfbDeviceManager.GetAxisOffsets</c>).</summary>
    [ObservableProperty]
    private int _axisIndex;

    /// <summary>DirectInput per-effect gain (0-10000, master volume for this effect).</summary>
    [ObservableProperty]
    private int _gain = 10000;

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
