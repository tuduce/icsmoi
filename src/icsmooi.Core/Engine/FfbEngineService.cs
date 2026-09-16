using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using icsmooi.Models;

namespace icsmooi.Engine;

/// <summary>
/// Runs the <see cref="GraphEvaluator"/> at a fixed frequency on a dedicated
/// <see cref="ThreadPool"/> thread via <see cref="System.Threading.Timer"/>.
///
/// <para><b>Thread safety:</b> <see cref="SimData"/> is a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> so it can be written from
/// the Phase 5 SimConnect telemetry thread while the engine timer reads it.
/// All other state is only touched from the timer callback or from the UI
/// thread in <see cref="Start"/>/<see cref="Stop"/>.</para>
///
/// <para><b>UI-technology-agnostic:</b> <see cref="OutputsUpdated"/> fires
/// directly on the timer thread — this service has no Avalonia UI-thread
/// dependency, since it's shared by both the Editor (which marshals to its
/// own UI thread in its subscriber) and the headless Runtime app.</para>
/// </summary>
public sealed class FfbEngineService : IDisposable
{
    private readonly GraphEvaluator _evaluator = new();
    private Timer? _timer;

    // Graph snapshot — updated atomically via volatile reference swap.
    // The timer always reads a consistent snapshot even if the UI thread
    // swaps it between ticks.
    private volatile EngineSnapshot? _snapshot;

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Thread-safe cache of SimConnect variable values.
    /// <list type="bullet">
    ///   <item>Phase 3 – populate with mock data to test the graph.</item>
    ///   <item>Phase 5 – <c>SimConnectTelemetryService</c> writes here continuously.</item>
    /// </list>
    /// </summary>
    public ConcurrentDictionary<string, double> SimData { get; } = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fired on <b>this service's timer thread</b> after every successful evaluation
    /// tick — callers that update UI-bound state must marshal to their own UI thread.
    /// Outputs are keyed by <see cref="FfbOutputNodeViewModel.Id"/>.
    /// </summary>
    public event Action<IReadOnlyDictionary<Guid, double>>? OutputsUpdated;

    /// <summary>Whether the engine timer is active.</summary>
    public bool IsRunning { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts (or restarts) the engine at <paramref name="intervalMs"/> ms
    /// intervals and takes an initial snapshot of <paramref name="profile"/>.
    /// </summary>
    public void Start(FfbProfile profile, int intervalMs = 10)
    {
        Stop();
        RefreshSnapshot(profile);
        IsRunning = true;
        _timer = new Timer(Tick, null, 0, intervalMs);
    }

    /// <summary>Stops the engine timer. Safe to call when already stopped.</summary>
    public void Stop()
    {
        IsRunning = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Re-snapshots the graph.  Call whenever nodes or connections are added,
    /// removed, or edited so the engine picks up the latest topology.
    /// </summary>
    public void RefreshSnapshot(FfbProfile profile)
    {
        _snapshot = new EngineSnapshot(
            profile.Nodes.ToArray(),
            profile.Connections.ToArray());
    }

    public void Dispose() => Stop();

    // ── Timer callback ────────────────────────────────────────────────────────

    private void Tick(object? _)
    {
        if (!IsRunning) return;

        var snap = _snapshot;
        if (snap is null) return;

        var result = _evaluator.Evaluate(snap.Nodes, snap.Connections, SimData);
        if (result is null) return; // Cycle — skip tick

        // Fired on this timer thread — callers marshal to their own UI thread if needed.
        var outputs = (IReadOnlyDictionary<Guid, double>)result;
        OutputsUpdated?.Invoke(outputs);
    }

    // ── Private snapshot record ───────────────────────────────────────────────

    private sealed record EngineSnapshot(
        NodeViewModel[] Nodes,
        ConnectionViewModel[] Connections);
}
