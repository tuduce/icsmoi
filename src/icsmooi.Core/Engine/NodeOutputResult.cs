using System;
using System.Collections.Generic;

namespace icsmooi.Engine;

/// <summary>
/// Result of one <see cref="GraphEvaluator.Evaluate"/> tick.
/// </summary>
/// <param name="Magnitudes">
/// Node ID → single-scalar magnitude, for UI readouts (e.g. <c>LastMagnitude</c>
/// bindings). Populated for every output-shaped node, including the legacy
/// placeholder <see cref="Models.FfbOutputNodeViewModel"/>.
/// </param>
/// <param name="EffectParams">
/// Node ID → richer per-effect-type parameters, for real DirectInput hardware
/// output. Only populated for node types that actually drive hardware (e.g.
/// <see cref="Models.ConstantForceOutputNodeViewModel"/>,
/// <see cref="Models.ConditionOutputNodeViewModel"/>) — the Editor never
/// consumes this, only <c>icsmooi.Runtime</c> does.
/// </param>
public sealed record NodeOutputResult(
    Dictionary<Guid, double> Magnitudes,
    Dictionary<Guid, EffectOutputParams> EffectParams);
