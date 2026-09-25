using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>
/// The graph's first stateful node: keeps a running total of <c>Input × dt</c> between engine ticks
/// (the total lives in the engine, keyed by node Id — see <c>FfbEngineService</c> — not on this view model,
/// which the timer thread must not write to). <c>Input</c> is a rate per second, so wiring a speed in m/s
/// gives a distance in metres, and (button − button) × rate gives a trim value.
/// <para>
/// <c>Wrap</c> &gt; 0 wraps the total into <c>[0, Wrap)</c> (a repeating distance/phase) and ignores
/// <c>Min</c>/<c>Max</c>; otherwise the total is clamped to <c>[Min, Max]</c>. While <c>Reset</c> is non-zero
/// the total is held at 0.
/// </para>
/// </summary>
public partial class IntegratorNodeViewModel : NodeViewModel
{
    /// <summary>
    /// When true, <c>Input</c> is added once per engine evaluation instead of being multiplied by the elapsed
    /// time — for feeding it events (an Edge Detector's one-tick pulse × a step size = "one step per press").
    /// The graph can't know <c>dt</c>, so a pulse can't be scaled to an exact step any other way.
    /// </summary>
    [ObservableProperty]
    private bool _addPerTick;

    public IntegratorNodeViewModel()
    {
        Name = "Integrator";
        Inputs.Add(new PinViewModel { Title = "Input", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Reset", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Min", IsInput = true, Value = -1.0 });
        Inputs.Add(new PinViewModel { Title = "Max", IsInput = true, Value = 1.0 });
        Inputs.Add(new PinViewModel { Title = "Wrap", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Result", IsInput = false });
    }
}
