using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>
/// Common base of the real-hardware output nodes (Constant Force, Condition,
/// Periodic, Ramp): the device/axis/gain targeting every one of them needs,
/// so the Editor can render a single shared settings block for all four.
/// </summary>
public abstract partial class HardwareOutputNodeViewModel : NodeViewModel
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
}
