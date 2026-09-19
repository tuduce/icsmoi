using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>What a joystick output pin reads: nothing yet, an analog axis/slider, or a push button.</summary>
public enum JoystickInputKind { None, Axis, Button }

/// <summary>
/// The analog inputs of a DirectInput joystick state (X/Y/Z, the three rotations and two
/// sliders). Sliders are just axes to DirectInput, so a throttle lever shows up here too.
/// </summary>
public enum JoystickAxis { X, Y, Z, RotationX, RotationY, RotationZ, Slider0, Slider1 }

/// <summary>
/// Shared naming for joystick inputs, so the polling service that writes values, the evaluator
/// that reads them and the UI that displays them can't drift apart.
/// </summary>
public static class JoystickInputKey
{
    public const int ButtonCount = 128;
    public static readonly int AxisCount = Enum.GetValues<JoystickAxis>().Length;

    /// <summary>Key under which a joystick input's live value lives in <c>FfbEngineService.SimData</c>.</summary>
    public static string For(Guid device, JoystickInputKind kind, int index) =>
        $"joy:{device:N}:{(kind == JoystickInputKind.Button ? 'b' : 'a')}{index}";

    /// <summary>Human-readable name (buttons are 1-based, like Windows' game-controller panel and the sims).</summary>
    public static string DisplayName(JoystickInputKind kind, int index) => kind switch
    {
        JoystickInputKind.Button => $"Button {index + 1}",
        JoystickInputKind.Axis => (JoystickAxis)index switch
        {
            JoystickAxis.X => "X Axis",
            JoystickAxis.Y => "Y Axis",
            JoystickAxis.Z => "Z Axis",
            JoystickAxis.RotationX => "X Rotation",
            JoystickAxis.RotationY => "Y Rotation",
            JoystickAxis.RotationZ => "Z Rotation",
            JoystickAxis.Slider0 => "Slider 1",
            JoystickAxis.Slider1 => "Slider 2",
            _ => $"Axis {index}",
        },
        _ => "",
    };
}

/// <summary>
/// One output of a <see cref="JoystickInputNodeViewModel"/>: a single joystick axis or button.
/// A pin subclass (not a parallel list on the node) so the binding lives on the pin itself and
/// the Editor can lay each binding out as one row beside its own connector.
/// </summary>
public partial class JoystickPinViewModel : PinViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private JoystickInputKind _kind;

    /// <summary>A <see cref="JoystickAxis"/> ordinal for axes, the zero-based button number for buttons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private int _index;

    /// <summary>Runtime-only: the Editor is waiting for the user to move an axis / press a button.</summary>
    [property: JsonIgnore] // `property:` — a plain [JsonIgnore] on the field never reaches the generated property
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isListening;

    /// <summary>What the pin's input field shows.</summary>
    [JsonIgnore]
    public string DisplayText =>
        IsListening ? "Move axis / press button…"
        : Kind == JoystickInputKind.None ? "Click to assign…"
        : JoystickInputKey.DisplayName(Kind, Index);

    public JoystickPinViewModel()
    {
        IsInput = false;
        Title = "Input";
    }

    public void Assign(JoystickInputKind kind, int index)
    {
        Kind = kind;
        Index = index;
        Title = JoystickInputKey.DisplayName(kind, index);
    }
}

/// <summary>
/// A physical joystick (any DirectInput game controller — it needn't have force feedback) exposed
/// as graph inputs: one output pin per bound axis/slider/button. Axes output a value normalized to
/// [-1, 1]; buttons output 1.0 while pressed and 0.0 otherwise (the graph's all-double wire convention).
/// Live values come from <c>JoystickInputService</c>, which publishes them into the same
/// <c>SimData</c> dictionary SimConnect telemetry uses, keyed by <see cref="JoystickInputKey.For"/>.
/// </summary>
public partial class JoystickInputNodeViewModel : NodeViewModel
{
    /// <summary>Which joystick to read — null until the user picks one.</summary>
    [ObservableProperty]
    private Guid? _deviceInstanceGuid;

    public JoystickInputNodeViewModel()
    {
        Name = "Joystick Input";
        Outputs.Add(new JoystickPinViewModel());
    }
}
