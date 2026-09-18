using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>DirectInput's waveform-specific effect GUIDs (there is no single "Periodic" GUID).</summary>
public enum Waveform { Sine, Square, Triangle, SawtoothUp, SawtoothDown }

/// <summary>
/// A real DirectInput periodic-force effect (sine/square/triangle/sawtooth,
/// picked via <see cref="Waveform"/>). Generalizes TDX-Air-Mechanics'
/// hardcoded stick-shaker/ground-vibration effects — layering multiple
/// Periodic nodes with different Period/Phase (fed by Math/Select nodes
/// instead of branching C#) reproduces that behavior visually.
/// </summary>
public partial class PeriodicOutputNodeViewModel : NodeViewModel
{
    [ObservableProperty]
    private Waveform _waveform = Waveform.Sine;

    [ObservableProperty]
    private Guid? _deviceInstanceGuid;

    [ObservableProperty]
    private int _axisIndex;

    [ObservableProperty]
    private int _gain = 10000;

    [JsonIgnore]
    [ObservableProperty]
    private double _lastMagnitude;

    public static readonly Waveform[] Waveforms = Enum.GetValues<Waveform>();

    public PeriodicOutputNodeViewModel()
    {
        Name = "Periodic";
        Inputs.Add(new PinViewModel { Title = "Magnitude", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Period", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Phase", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Offset", IsInput = true });
    }
}
