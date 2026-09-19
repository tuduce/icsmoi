using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>One entry of a hardware-output node's axis dropdown: <see cref="HardwareOutputNodeViewModel.AllAxes"/> or an index into the device's FFB axes.</summary>
public sealed record AxisChoice(int Index, string Name);

/// <summary>
/// Common base of the real-hardware output nodes (Constant Force, Condition,
/// Periodic, Ramp): the device/axis/gain targeting every one of them needs,
/// so the Editor can render a single shared settings block for all four.
/// </summary>
public abstract partial class HardwareOutputNodeViewModel : NodeViewModel
{
    /// <summary><see cref="AxisIndex"/> value meaning "every force axis of the device".</summary>
    public const int AllAxes = -1;

    /// <summary>Which physical FFB device this effect targets — null until the user picks one.</summary>
    [ObservableProperty]
    private Guid? _deviceInstanceGuid;

    /// <summary>
    /// Which force axis the effect drives: an index into the device's FFB axes
    /// (<c>FfbDeviceManager.GetAxes</c>, ordered by HID usage — X, then Y), or <see cref="AllAxes"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllAxes))]
    [NotifyPropertyChangedFor(nameof(ShowsDirection))]
    [NotifyPropertyChangedFor(nameof(SelectedAxis))]
    private int _axisIndex = AllAxes;

    /// <summary>
    /// Where the force pushes the stick when <see cref="AxisIndex"/> is <see cref="AllAxes"/>:
    /// 0° = forward (away from the pilot), clockwise — see <c>EffectDirection</c>.
    /// </summary>
    [ObservableProperty]
    private double _directionDegrees;

    /// <summary>DirectInput per-effect gain (0-10000, master volume for this effect).</summary>
    [ObservableProperty]
    private int _gain = 10000;

    /// <summary>Runtime-only: what the axis dropdown offers for the selected device (filled in by the Editor, which knows the device list).</summary>
    [property: JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAxis))] // raised *after* AxisChoices itself, so the dropdown has its new items before it re-selects
    private IReadOnlyList<AxisChoice> _axisChoices = [new AxisChoice(AllAxes, "All axes")];

    /// <summary>
    /// The dropdown's selection, derived from <see cref="AxisIndex"/>. The ComboBox binds to this rather than
    /// using SelectedValue: SelectedValue loses a non-zero selection when the items arrive after the binding
    /// (the profile-load order), leaving the dropdown blank. Null (nothing selected) when the saved axis isn't
    /// among the current device's — e.g. the device isn't plugged in — and never written back.
    /// </summary>
    [JsonIgnore]
    public AxisChoice? SelectedAxis
    {
        get => AxisChoices.FirstOrDefault(choice => choice.Index == AxisIndex);
        set
        {
            if (value is not null) AxisIndex = value.Index;
        }
    }

    [JsonIgnore]
    public bool IsAllAxes => AxisIndex == AllAxes;

    /// <summary>
    /// Whether a direction is meaningful for this effect type. Force-like effects (constant, periodic, ramp)
    /// are split across the axes by it; Condition effects (spring/damper/inertia/friction) aren't — "all axes"
    /// applies the same condition to every axis, like a centering spring.
    /// </summary>
    [JsonIgnore]
    public virtual bool SupportsDirection => true;

    /// <summary>The direction dial is shown only when it would have an effect.</summary>
    [JsonIgnore]
    public bool ShowsDirection => IsAllAxes && SupportsDirection;
}
