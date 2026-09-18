using System;
using System.Collections.Generic;

namespace icsmoi.Engine;

/// <summary>
/// Result of one <see cref="GraphEvaluator.Evaluate"/> tick.
/// </summary>
/// <param name="Magnitudes">
/// Node ID → single-scalar magnitude, for UI readouts (e.g. <c>LastMagnitude</c>
/// bindings). Populated for every hardware-output node.
/// </param>
/// <param name="EffectParams">
/// Node ID → richer per-effect-type parameters, for real DirectInput hardware
/// output. Only populated for node types that actually drive hardware (e.g.
/// <see cref="Models.ConstantForceOutputNodeViewModel"/>,
/// <see cref="Models.ConditionOutputNodeViewModel"/>) — the Editor never
/// consumes this, only <c>icsmoi.Runtime</c> does.
/// </param>
public sealed record NodeOutputResult(
    Dictionary<Guid, double> Magnitudes,
    Dictionary<Guid, EffectOutputParams> EffectParams);
