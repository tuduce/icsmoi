namespace icsmooi.Engine;

/// <summary>
/// Per-tick, per-effect-type parameters for a real DirectInput effect —
/// richer than the single scalar magnitude used for UI readouts, since e.g. a
/// Condition (spring/damper/friction/inertia) effect needs several
/// coefficients, not one number. One subtype per output-node "shape".
/// </summary>
public abstract record EffectOutputParams;

public sealed record ConstantForceParams(double Magnitude) : EffectOutputParams;

public sealed record ConditionParams(
    double PositiveCoefficient,
    double NegativeCoefficient,
    double Offset,
    double DeadBand,
    double Saturation) : EffectOutputParams;
