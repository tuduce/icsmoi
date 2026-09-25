using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using icsmoi.Models;

namespace icsmoi.Engine;

/// <summary>
/// Runs the <see cref="GraphEvaluator"/> (and holds the state of its stateful nodes) at a fixed frequency on a dedicated
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

    // Running values of stateful nodes (Integrator), keyed by node Id. Only the timer thread touches the
    // dictionary a tick captured; Start swaps in a fresh one (a profile switch or engine restart begins
    // from zero) rather than clearing it, so a tick still in flight can't race the reset. A hot reload
    // only calls RefreshSnapshot, so node Ids — and their totals — carry over.
    private volatile Dictionary<Guid, double> _nodeState = new();
    private EngineSnapshot? _prunedForSnapshot;
    private long _lastTickTimestamp;

    // A stalled process (debugger, suspend) must not integrate one enormous step when it wakes.
    private const double MaxDeltaSeconds = 0.25;

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
    /// Outputs are keyed by the output node's <see cref="NodeViewModel.Id"/>.
    /// </summary>
    public event Action<IReadOnlyDictionary<Guid, double>>? OutputsUpdated;

    /// <summary>
    /// Fired on the same timer thread as <see cref="OutputsUpdated"/>, carrying
    /// richer per-effect-type parameters for real hardware-output node types.
    /// Only <c>icsmoi.Runtime</c>'s <c>FfbEffectManager</c> subscribes to this —
    /// the Editor never drives real FFB hardware.
    /// </summary>
    public event Action<IReadOnlyDictionary<Guid, EffectOutputParams>>? HardwareOutputsUpdated;

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
        _nodeState = new Dictionary<Guid, double>();
        _lastTickTimestamp = 0;
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

    // 1 while a tick is in flight. System.Threading.Timer fires its callback on the thread pool every
    // period *whether or not the previous call has returned*, so a tick slower than the period — a real
    // force-feedback device call takes ~16 ms against this 10 ms period — used to pile up dozens of
    // concurrent ticks. They raced each other: each saw "no effect yet" and created one (13 effects in
    // 5 s, orphans left playing and adding up) and hammered the same USB device. A tick that finds
    // another still running now just skips; the next one reads the newest values anyway.
    private int _tickRunning;

    private void Tick(object? _)
    {
        if (!IsRunning) return;
        if (Interlocked.Exchange(ref _tickRunning, 1) == 1) return;

        try
        {
            var snap = _snapshot;
            if (snap is null) return;

            // Real elapsed time, not the nominal period: ticks are skipped while a device call is slow.
            var now = Stopwatch.GetTimestamp();
            var last = _lastTickTimestamp;
            _lastTickTimestamp = now;
            var deltaSeconds = last == 0
                ? 0.0
                : Math.Min(Stopwatch.GetElapsedTime(last, now).TotalSeconds, MaxDeltaSeconds);

            var nodeState = _nodeState;
            if (!ReferenceEquals(_prunedForSnapshot, snap))
            {
                // The graph changed: forget totals of nodes that are gone.
                var liveIds = snap.Nodes.Select(n => n.Id).ToHashSet();
                foreach (var id in nodeState.Keys.Where(k => !liveIds.Contains(k)).ToList())
                    nodeState.Remove(id);
                _prunedForSnapshot = snap;
            }

            NodeOutputResult? result;
            try
            {
                result = _evaluator.Evaluate(snap.Nodes, snap.Connections, SimData, nodeState, deltaSeconds);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
            {
                // The Editor can add/remove a node's pins (Joystick Input rows) on the UI thread while
                // this tick enumerates them. An exception escaping a Timer callback would take the whole
                // process down, so treat a mid-edit graph like a cyclic one: skip this tick.
                return;
            }
            if (result is null) return; // Cycle — skip tick

            // Fired on this timer thread — callers marshal to their own UI thread if needed.
            OutputsUpdated?.Invoke(result.Magnitudes);
            HardwareOutputsUpdated?.Invoke(result.EffectParams);
        }
        finally
        {
            Volatile.Write(ref _tickRunning, 0);
        }
    }

    // ── Private snapshot record ───────────────────────────────────────────────

    private sealed record EngineSnapshot(
        NodeViewModel[] Nodes,
        ConnectionViewModel[] Connections);
}
