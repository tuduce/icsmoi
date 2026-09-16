using System;
using System.Collections.Generic;
using System.Linq;
using NCalc;
using icsmooi.Models;

namespace icsmooi.Engine;

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
///   <item><see cref="FfbOutputNodeViewModel"/> – collects the final
///         magnitude from its single input wire.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GraphEvaluator
{
    /// <summary>
    /// Evaluates the graph and returns the output magnitude for every
    /// <see cref="FfbOutputNodeViewModel"/> found in <paramref name="nodes"/>.
    /// </summary>
    /// <param name="nodes">Snapshot of all nodes in the profile.</param>
    /// <param name="connections">Snapshot of all connections.</param>
    /// <param name="simData">Current SimConnect variable values (variable name → value).
    ///   Phase 4 will supply a <c>ConcurrentDictionary</c> here; Phase 3 passes a
    ///   read-only mock.</param>
    /// <returns>
    ///   A dictionary mapping each <see cref="FfbOutputNodeViewModel.Id"/> to its
    ///   computed magnitude, or <c>null</c> if the graph contains a cycle.
    /// </returns>
    public Dictionary<Guid, double>? Evaluate(
        IReadOnlyList<NodeViewModel> nodes,
        IReadOnlyList<ConnectionViewModel> connections,
        IReadOnlyDictionary<string, double> simData)
    {
        if (nodes.Count == 0) return [];

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

        var wireValues = new Dictionary<Guid, double>(
            nodes.Sum(n => n.Inputs.Count + n.Outputs.Count));
        foreach (var node in nodes)
            foreach (var pin in node.Inputs.Concat(node.Outputs))
                wireValues[pin.Id] = 0.0;

        // ── 6.  Evaluate nodes in topological order ───────────────────────────

        var outputs = new Dictionary<Guid, double>();

        foreach (var nodeId in evalOrder)
        {
            if (!nodeById.TryGetValue(nodeId, out var node)) continue;

            switch (node)
            {
                case SimConnectNodeViewModel sc:
                    EvalSimConnect(sc, simData, wireValues);
                    break;

                case MathNodeViewModel math:
                    EvalMath(math, wireValues);
                    break;

                case FfbOutputNodeViewModel ffb:
                    var pin = ffb.Inputs.FirstOrDefault();
                    outputs[ffb.Id] = pin is not null && wireValues.TryGetValue(pin.Id, out var mag)
                        ? mag : 0.0;
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

        return outputs;
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
}
