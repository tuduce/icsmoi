using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace icsmoi.Models;

/// <summary>DirectInput's four Condition-family effects share one parameter shape (ConditionSet) — only the effect GUID differs.</summary>
public enum ConditionKind { Spring, Damper, Inertia, Friction }

/// <summary>
/// A real DirectInput Condition effect (spring/damper/inertia/friction, picked
/// via <see cref="ConditionKind"/>) — one node type covers all four since they
/// share the same coefficient/offset/deadband/saturation parameter shape.
/// This is the generalization of TDX-Air-Mechanics' hardcoded sigmoid-curve
/// spring: the coefficient inputs are normally fed by a Curve or Math node
/// instead of hardcoded C#.
/// </summary>
public partial class ConditionOutputNodeViewModel : HardwareOutputNodeViewModel
{
    [ObservableProperty]
    private ConditionKind _conditionKind = ConditionKind.Spring;

    [JsonIgnore]
    [ObservableProperty]
    private double _lastPositiveCoefficient;

    public static readonly ConditionKind[] ConditionKinds = Enum.GetValues<ConditionKind>();

    // Conditions are per-axis (one condition on each axis) — a direction doesn't apply.
    // ([JsonIgnore] is not inherited by an override, so it is repeated here.)
    [JsonIgnore]
    public override bool SupportsDirection => false;

    public ConditionOutputNodeViewModel()
    {
        Name = "Condition";
        Inputs.Add(new PinViewModel { Title = "PositiveCoefficient", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "NegativeCoefficient", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "Offset", IsInput = true });
        Inputs.Add(new PinViewModel { Title = "DeadBand", IsInput = true });
        // Full saturation, so a coefficient alone (constant or wired) already produces force.
        Inputs.Add(new PinViewModel { Title = "Saturation", IsInput = true, Value = 1.0 });
    }
}
