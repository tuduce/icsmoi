namespace icsmoi.Models;

/// <summary>
/// Turns a level into events: each output is 1.0 for exactly one engine evaluation when the input changes,
/// otherwise 0.0. The input follows the graph's bool convention (non-zero = high), so a joystick button,
/// a Comparison/Logic result or any other wire works.
/// <list type="bullet">
///   <item><c>Rising</c> — the input went low → high.</item>
///   <item><c>Falling</c> — the input went high → low.</item>
///   <item><c>Either</c> — either edge.</item>
/// </list>
/// Stateful (the previous level lives in the engine, keyed by node Id — see <c>FfbEngineService</c>).
/// Nothing fires on the very first evaluation, so an input that is already high when the engine starts
/// or the profile switches is not a press.
/// </summary>
public partial class EdgeDetectorNodeViewModel : NodeViewModel
{
    public EdgeDetectorNodeViewModel()
    {
        Name = "Edge Detector";
        Inputs.Add(new PinViewModel { Title = "Input", IsInput = true });
        Outputs.Add(new PinViewModel { Title = "Rising", IsInput = false });
        Outputs.Add(new PinViewModel { Title = "Falling", IsInput = false });
        Outputs.Add(new PinViewModel { Title = "Either", IsInput = false });
    }
}
