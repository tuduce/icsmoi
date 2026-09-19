using System;
using System.Collections.Generic;
using System.Linq;
using NCalc;
using icsmoi.Models;

namespace icsmoi.Engine;

/// <summary>
/// Pure, stateless graph evaluator.
/// <para>
/// Accepts a snapshot of nodes and connections, performs a topological sort
/// (Kahn's algorithm) to detect cycles, then evaluates each node in dependency
/// order:
/// <list type="bullet">
///   <item><see cref="SimConnectNodeViewModel"/> – reads a value from the
///         supplied <paramref name="simData"/> cache.</item>
///   <item><see cref="MathNodeViewModel"/> – evaluates the NCalc expression,
///         mapping pin titles (e.g. "A", "B") to the wired input values.</item>
///   <item>The typed hardware-output nodes (Constant Force, Condition,
///         Periodic, Ramp) – collect their input-pin values into
///         per-effect parameters.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GraphEvaluator
{
    /// <summary>
    /// Evaluates the graph and returns both a UI-readout magnitude and (for
    /// real hardware-output node types) richer per-effect parameters for
    /// every output node found in <paramref name="nodes"/>.
    /// </summary>
    /// <param name="nodes">Snapshot of all nodes in the profile.</param>
    /// <param name="connections">Snapshot of all connections.</param>
    /// <param name="simData">Current SimConnect variable values (variable name → value).</param>
    /// <returns>The evaluated outputs, or <c>null</c> if the graph contains a cycle.</returns>
    public NodeOutputResult? Evaluate(
        IReadOnlyList<NodeViewModel> nodes,
        IReadOnlyList<ConnectionViewModel> connections,
        IReadOnlyDictionary<string, double> simData)
    {
        if (nodes.Count == 0) return new NodeOutputResult([], []);

        // ── 1.  Build lookup tables ─────────────────────────────────────────

        var nodeById  = nodes.ToDictionary(n => n.Id);

        // pin ID → owning node ID
        var pinOwner = new Dictionary<Guid, Guid>(
            nodes.Sum(n => n.Inputs.Count + n.Outputs.Count));
        foreach (var node in nodes)
            foreach (var pin in node.Inputs.Concat(node.Outputs))
                pinOwner[pin.Id] = node.Id;

        // ── 2.  Build directed-graph for topological sort ───────────────────
        //   An edge A → B means "A's output feeds B's input".

        var dependents = nodes.ToDictionary(n => n.Id, _ => new List<Guid>());
        var inDegree   = nodes.ToDictionary(n => n.Id, _ => 0);

        foreach (var conn in connections)
        {
            if (!pinOwner.TryGetValue(conn.SourcePinId, out var srcId)) continue;
            if (!pinOwner.TryGetValue(conn.TargetPinId, out var tgtId)) continue;
            if (srcId == tgtId) continue;   // ignore self-loops

            dependents[srcId].Add(tgtId);
            inDegree[tgtId]++;
        }

        // ── 3.  Kahn's algorithm (topological sort + cycle detection) ────────

        var queue = new Queue<Guid>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var evalOrder = new List<Guid>(nodes.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            evalOrder.Add(current);

            foreach (var dependent in dependents[current])
                if (--inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
        }

        if (evalOrder.Count != nodes.Count)
            return null; // Cycle detected — refuse to evaluate

        // ── 4.  Build forward-propagation map ────────────────────────────────
        //   source pin ID → list of target pin IDs (fan-out support)

        var wireMap = new Dictionary<Guid, List<Guid>>();
        foreach (var conn in connections)
        {
            if (!wireMap.TryGetValue(conn.SourcePinId, out var targets))
                wireMap[conn.SourcePinId] = targets = [];
            targets.Add(conn.TargetPinId);
        }

        // ── 5.  Initialise wire-value table ──────────────────────────────────
        //   An input pin starts at its own constant (PinViewModel.Value); a wired
        //   input is overwritten by its source's output when that node propagates.

        var wireValues = new Dictionary<Guid, double>(
            nodes.Sum(n => n.Inputs.Count + n.Outputs.Count));
        foreach (var node in nodes)
        {
            foreach (var pin in node.Inputs)
                wireValues[pin.Id] = pin.Value;
            foreach (var pin in node.Outputs)
                wireValues[pin.Id] = 0.0;
        }

        // ── 6.  Evaluate nodes in topological order ───────────────────────────

        var magnitudes = new Dictionary<Guid, double>();
        var effectParams = new Dictionary<Guid, EffectOutputParams>();

        foreach (var nodeId in evalOrder)
        {
            if (!nodeById.TryGetValue(nodeId, out var node)) continue;

            switch (node)
            {
                case SimConnectNodeViewModel sc:
                    EvalSimConnect(sc, simData, wireValues);
                    break;

                case JoystickInputNodeViewModel joystick:
                    EvalJoystick(joystick, simData, wireValues);
                    break;

                case MathNodeViewModel math:
                    EvalMath(math, wireValues);
                    break;

                case ComparisonNodeViewModel cmp:
                    EvalComparison(cmp, wireValues);
                    break;

                case LogicNodeViewModel logic:
                    EvalLogic(logic, wireValues);
                    break;

                case SelectNodeViewModel select:
                    EvalSelect(select, wireValues);
                    break;

                case ClampNodeViewModel clamp:
                    EvalClamp(clamp, wireValues);
                    break;

                case RangeMapNodeViewModel rangeMap:
                    EvalRangeMap(rangeMap, wireValues);
                    break;

                case CurveNodeViewModel curve:
                    EvalCurve(curve, wireValues);
                    break;

                case ConstantForceOutputNodeViewModel constantForce:
                    var cfMagnitude = GetPinValue(constantForce, "Magnitude", wireValues);
                    magnitudes[constantForce.Id] = cfMagnitude;
                    effectParams[constantForce.Id] = new ConstantForceParams(cfMagnitude);
                    break;

                case ConditionOutputNodeViewModel condition:
                    var posCoeff = GetPinValue(condition, "PositiveCoefficient", wireValues);
                    var negCoeff = GetPinValue(condition, "NegativeCoefficient", wireValues);
                    var offset = GetPinValue(condition, "Offset", wireValues);
                    var deadBand = GetPinValue(condition, "DeadBand", wireValues);
                    var saturation = GetPinValue(condition, "Saturation", wireValues);
                    magnitudes[condition.Id] = posCoeff;
                    effectParams[condition.Id] = new ConditionParams(posCoeff, negCoeff, offset, deadBand, saturation);
                    break;

                case PeriodicOutputNodeViewModel periodic:
                    var pMagnitude = GetPinValue(periodic, "Magnitude", wireValues);
                    var period = GetPinValue(periodic, "Period", wireValues);
                    var phase = GetPinValue(periodic, "Phase", wireValues);
                    var pOffset = GetPinValue(periodic, "Offset", wireValues);
                    magnitudes[periodic.Id] = pMagnitude;
                    effectParams[periodic.Id] = new PeriodicParams(periodic.Waveform, pMagnitude, period, phase, pOffset);
                    break;

                case RampForceOutputNodeViewModel ramp:
                    var startMagnitude = GetPinValue(ramp, "StartMagnitude", wireValues);
                    var endMagnitude = GetPinValue(ramp, "EndMagnitude", wireValues);
                    var duration = GetPinValue(ramp, "Duration", wireValues);
                    magnitudes[ramp.Id] = startMagnitude;
                    effectParams[ramp.Id] = new RampParams(startMagnitude, endMagnitude, duration);
                    break;
            }

            // Propagate this node's output pin values to every connected downstream pin
            foreach (var outPin in node.Outputs)
            {
                if (!wireMap.TryGetValue(outPin.Id, out var targets)) continue;
                var value = wireValues.TryGetValue(outPin.Id, out var v) ? v : 0.0;
                foreach (var tgt in targets)
                    wireValues[tgt] = value;
            }
        }

        return new NodeOutputResult(magnitudes, effectParams);
    }

    // ── Node-type evaluation helpers ─────────────────────────────────────────

    private static void EvalSimConnect(
        SimConnectNodeViewModel node,
        IReadOnlyDictionary<string, double> simData,
        Dictionary<Guid, double> wireValues)
    {
        var value = simData.TryGetValue(node.VariableName, out var v) ? v : 0.0;

        var outPin = node.Outputs.FirstOrDefault();
        if (outPin is not null)
            wireValues[outPin.Id] = value;
    }

    private static void EvalJoystick(
        JoystickInputNodeViewModel node,
        IReadOnlyDictionary<string, double> simData,
        Dictionary<Guid, double> wireValues)
    {
        var device = node.DeviceInstanceGuid;

        foreach (var pin in node.Outputs)
        {
            // Unassigned pins and a node with no device read 0; so does a device that is unplugged
            // (JoystickInputService zeroes a device's keys when it loses it).
            var value = device is { } d && pin is JoystickPinViewModel { Kind: not JoystickInputKind.None } joystickPin
                && simData.TryGetValue(JoystickInputKey.For(d, joystickPin.Kind, joystickPin.Index), out var v)
                ? v
                : 0.0;
            wireValues[pin.Id] = value;
        }
    }

    private static void EvalMath(
        MathNodeViewModel node,
        Dictionary<Guid, double> wireValues)
    {
        // Map each input pin's Title (e.g. "A", "B") to its current wire value.
        // The NCalc expression references them as [A], [B].
        var expr = new Expression(node.Expression);
        foreach (var pin in node.Inputs)
        {
            var val = wireValues.TryGetValue(pin.Id, out var v) ? v : 0.0;
            expr.Parameters[pin.Title] = val;
        }

        double result = 0.0;
        try
        {
            result = Convert.ToDouble(expr.Evaluate());
        }
        catch
        {
            // Silently clamp to 0 on any expression error
            // (syntax error, division by zero, undefined reference, etc.)
        }

        var outPin = node.Outputs.FirstOrDefault();
        if (outPin is not null)
            wireValues[outPin.Id] = result;
    }

    private static void EvalComparison(ComparisonNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var a = GetPinValue(node, "A", wireValues);
        var b = GetPinValue(node, "B", wireValues);

        var result = node.Operator switch
        {
            ComparisonOperator.GreaterThan => a > b,
            ComparisonOperator.LessThan => a < b,
            ComparisonOperator.GreaterOrEqual => a >= b,
            ComparisonOperator.LessOrEqual => a <= b,
            ComparisonOperator.Equal => a == b,
            ComparisonOperator.NotEqual => a != b,
            _ => false,
        };

        SetOutputValue(node, result ? 1.0 : 0.0, wireValues);
    }

    private static void EvalLogic(LogicNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var a = GetPinValue(node, "A", wireValues) != 0.0;
        var b = GetPinValue(node, "B", wireValues) != 0.0;

        var result = node.Operator switch
        {
            LogicOperator.And => a && b,
            LogicOperator.Or => a || b,
            LogicOperator.Not => !a,
            LogicOperator.Xor => a ^ b,
            _ => false,
        };

        SetOutputValue(node, result ? 1.0 : 0.0, wireValues);
    }

    private static void EvalSelect(SelectNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var condition = GetPinValue(node, "Condition", wireValues) != 0.0;
        var value = condition
            ? GetPinValue(node, "IfTrue", wireValues)
            : GetPinValue(node, "IfFalse", wireValues);

        SetOutputValue(node, value, wireValues);
    }

    private static void EvalClamp(ClampNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var value = GetPinValue(node, "Value", wireValues);
        var min = GetPinValue(node, "Min", wireValues);
        var max = GetPinValue(node, "Max", wireValues);
        if (min > max) (min, max) = (max, min);

        SetOutputValue(node, Math.Clamp(value, min, max), wireValues);
    }

    private static void EvalRangeMap(RangeMapNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var value = GetPinValue(node, "Value", wireValues);
        var inMin = GetPinValue(node, "InMin", wireValues);
        var inMax = GetPinValue(node, "InMax", wireValues);
        var outMin = GetPinValue(node, "OutMin", wireValues);
        var outMax = GetPinValue(node, "OutMax", wireValues);

        var inSpan = inMax - inMin;
        var result = Math.Abs(inSpan) < double.Epsilon
            ? outMin
            : outMin + (value - inMin) / inSpan * (outMax - outMin);

        if (node.Clamp)
        {
            var lo = Math.Min(outMin, outMax);
            var hi = Math.Max(outMin, outMax);
            result = Math.Clamp(result, lo, hi);
        }

        SetOutputValue(node, result, wireValues);
    }

    private static void EvalCurve(CurveNodeViewModel node, Dictionary<Guid, double> wireValues)
    {
        var value = GetPinValue(node, "Value", wireValues);
        var points = node.Points;

        double result;
        if (points.Count == 0)
        {
            result = 0.0;
        }
        else if (points.Count == 1)
        {
            result = points[0].Y;
        }
        else
        {
            var sorted = points.OrderBy(p => p.X).ToList();
            if (value <= sorted[0].X)
            {
                result = sorted[0].Y;
            }
            else if (value >= sorted[^1].X)
            {
                result = sorted[^1].Y;
            }
            else
            {
                var upperIndex = sorted.FindIndex(p => p.X >= value);
                var lower = sorted[upperIndex - 1];
                var upper = sorted[upperIndex];
                var span = upper.X - lower.X;
                var t = span == 0 ? 0 : (value - lower.X) / span;
                result = lower.Y + t * (upper.Y - lower.Y);
            }
        }

        SetOutputValue(node, result, wireValues);
    }

    // ── Shared pin-access helpers for fixed-named-pin node types ─────────────

    private static double GetPinValue(NodeViewModel node, string pinTitle, Dictionary<Guid, double> wireValues)
    {
        var pin = node.Inputs.FirstOrDefault(p => p.Title == pinTitle);
        return pin is not null && wireValues.TryGetValue(pin.Id, out var v) ? v : 0.0;
    }

    private static void SetOutputValue(NodeViewModel node, double value, Dictionary<Guid, double> wireValues)
    {
        var outPin = node.Outputs.FirstOrDefault();
        if (outPin is not null)
            wireValues[outPin.Id] = value;
    }
}
