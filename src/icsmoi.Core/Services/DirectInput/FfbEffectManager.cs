using System;
using System.Collections.Generic;
using System.Linq;
using icsmoi.Engine;
using icsmoi.Models;
using Vortice.DirectInput;

namespace icsmoi.Services.DirectInput;

/// <summary>
/// Keeps one long-lived DirectInput <see cref="IDirectInputEffect"/> per
/// hardware-output node, created once and updated in place every tick via
/// <c>SetParameters(..., EffectParameterFlags.TypeSpecificParameters)</c> —
/// never recreated per tick. Only <c>icsmoi.Runtime</c> instantiates this;
/// the Editor stays hardware-agnostic (see <see cref="FfbEngineService.HardwareOutputsUpdated"/>).
/// </summary>
public sealed class FfbEffectManager : IDisposable
{
    private readonly FfbDeviceManager _deviceManager;
    private readonly IntPtr _windowHandle;
    private readonly Dictionary<Guid, EffectState> _effects = new();
    private readonly Dictionary<Guid, long> _retryAt = new();

    // DIEB_NOTRIGGER. The field defaults to 0, which with ObjectOffsets names offset 0 — the Y *axis* on the
    // TDX Force — as the effect's trigger button, and CreateEffect rejects that with DIERR_INVALIDPARAM.
    // (Verified against the real device: every effect failed until this was set; the sibling TDX effects set it too.)
    private const int NoTriggerButton = -1;

    // How long a node whose effect failed is left alone before another attempt (the tick runs at 100 Hz).
    private const long RetryDelayMs = 3000;

    /// <summary>
    /// Raised (on the engine's timer thread) when creating or updating a node's effect throws — a device
    /// that can't be acquired, parameters the driver rejects, a device that vanished. The node's effect is torn
    /// down and retried after a few seconds; the rest of the graph keeps running.
    /// </summary>
    public event Action<HardwareOutputNodeViewModel, Exception>? EffectFailed;

    /// <summary>How many DirectInput effects this manager has created — should equal the number of hardware-output nodes, not grow with time.</summary>
    public int EffectsCreated { get; private set; }

    // Serializes SyncTick and Dispose. The engine no longer overlaps ticks, but the Runtime disposes this
    // from the UI thread when the Effects toggle flips, which used to race a tick in flight.
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>What was last sent for a node's effect, so a changed setting can be applied (or the effect rebuilt).</summary>
    private sealed class EffectState(IDirectInputEffect effect, Guid effectGuid, int axisIndex, int gain, double directionDegrees, long[] key)
    {
        public IDirectInputEffect Effect { get; } = effect;
        public Guid EffectGuid { get; } = effectGuid;
        public int AxisIndex { get; } = axisIndex;
        public int Gain { get; set; } = gain;
        public double DirectionDegrees { get; set; } = directionDegrees;

        /// <summary>The type-specific parameters last sent, in DirectInput units — see <see cref="KeyOf"/>.</summary>
        public long[] Key { get; set; } = key;
    }

    public FfbEffectManager(FfbDeviceManager deviceManager, IntPtr windowHandle)
    {
        _deviceManager = deviceManager;
        _windowHandle = windowHandle;
    }

    /// <summary>Call once per engine tick with the current node snapshot and this tick's computed effect parameters.</summary>
    public void SyncTick(IReadOnlyList<NodeViewModel> nodes, IReadOnlyDictionary<Guid, EffectOutputParams> effectParams)
    {
        lock (_gate)
        {
            // A tick that captured this manager just before the Runtime's Effects toggle disposed it lands
            // here afterwards. Without this it called into the disposed DirectInput object, threw a
            // NullReferenceException, and reported that as an effect failure over the "Effects: Disabled" status.
            if (_disposed) return;
            SyncTickLocked(nodes, effectParams);
        }
    }

    private void SyncTickLocked(IReadOnlyList<NodeViewModel> nodes, IReadOnlyDictionary<Guid, EffectOutputParams> effectParams)
    {
        var liveIds = new HashSet<Guid>();

        foreach (var node in nodes)
        {
            switch (node)
            {
                case ConstantForceOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } cf:
                    liveIds.Add(cf.Id);
                    if (effectParams.TryGetValue(cf.Id, out var cfRaw) && cfRaw is ConstantForceParams cfParams)
                        TryUpdate(cf, deviceGuid, EffectGuid.ConstantForce, _ => BuildConstantForce(cfParams),
                            n => n.LastMagnitude = cfParams.Magnitude);
                    break;

                case ConditionOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } cond:
                    liveIds.Add(cond.Id);
                    if (effectParams.TryGetValue(cond.Id, out var condRaw) && condRaw is ConditionParams condParams)
                        TryUpdate(cond, deviceGuid, ConditionEffectGuid(cond.ConditionKind), active => BuildCondition(condParams, active),
                            n => n.LastPositiveCoefficient = condParams.PositiveCoefficient);
                    break;

                case PeriodicOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } periodic:
                    liveIds.Add(periodic.Id);
                    if (effectParams.TryGetValue(periodic.Id, out var periodicRaw) && periodicRaw is PeriodicParams periodicParams)
                        TryUpdate(periodic, deviceGuid, WaveformEffectGuid(periodic.Waveform), _ => BuildPeriodic(periodicParams),
                            n => n.LastMagnitude = periodicParams.Magnitude);
                    break;

                case RampForceOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } ramp:
                    liveIds.Add(ramp.Id);
                    if (effectParams.TryGetValue(ramp.Id, out var rampRaw) && rampRaw is RampParams rampParams)
                        TryUpdate(ramp, deviceGuid, EffectGuid.RampForce, _ => BuildRamp(rampParams),
                            n => n.LastStartMagnitude = rampParams.StartMagnitude);
                    break;
            }
        }

        // Node deleted, or lost its device assignment — stop and dispose its effect.
        foreach (var id in _effects.Keys.Except(liveIds).ToList())
            RemoveEffect(id);
        foreach (var id in _retryAt.Keys.Except(liveIds).ToList())
            _retryAt.Remove(id);
    }

    /// <summary>
    /// Creates the node's effect on first sight, then keeps it in step with the node. What can change
    /// while running: the type-specific parameters (every tick), gain and direction (applied in place when
    /// they change). What can't: the effect type (ConditionKind / Waveform) and which axes it drives —
    /// changing those rebuilds the effect.
    /// </summary>
    private void TryUpdate<TNode>(
        TNode node, Guid deviceGuid, Guid effectGuid,
        Func<IReadOnlyList<bool>, EffectParameters> buildParams, Action<TNode> applyReadout)
        where TNode : HardwareOutputNodeViewModel
    {
        if (_retryAt.TryGetValue(node.Id, out var due) && Environment.TickCount64 < due) return;

        try
        {
            var device = _deviceManager.AcquireDevice(deviceGuid, _windowHandle);
            var axes = _deviceManager.GetAxes(deviceGuid);

            var targets = ResolveTargetAxes(axes, node.AxisIndex);
            if (targets.Count == 0)
            {
                RemoveEffect(node.Id); // no such axis on this device (or a device without force axes): nothing to drive
                return;
            }

            _effects.TryGetValue(node.Id, out var state);
            if (state is not null && (state.EffectGuid != effectGuid || state.AxisIndex != node.AxisIndex))
            {
                RemoveEffect(node.Id);
                state = null;
            }

            // A condition effect always lists EVERY force axis, even when the node drives just one: the TDX_Force
            // firmware keeps condition parameters per axis in blocks 0 (roll) and 1 (pitch) and plays both, but
            // DirectInput numbers a condition's blocks by position in the effect's axis list — so a Y-only spring
            // arrived as block 0 and landed on roll (COM7 log: both a 20% Y spring and a 10% X spring said
            // "Set cond … axis=0"; nothing ever reached pitch). Listing both axes sends both blocks; the axes the
            // node doesn't drive get an all-zero condition, which produces no force.
            var effectAxes = node is ConditionOutputNodeViewModel ? axes : targets;
            var active = effectAxes.Select(a => targets.Contains(a)).ToArray();

            var parameters = buildParams(active);
            parameters.Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets;
            parameters.TriggerButton = NoTriggerButton;
            parameters.Gain = node.Gain;
            parameters.Axes = effectAxes.Select(a => a.Offset).ToArray();
            parameters.Directions = BuildDirections(effectAxes, node, deviceGuid);

            var key = KeyOf(parameters);
            if (state is null)
            {
                var effect = device.CreateEffect(effectGuid, parameters);
                try
                {
                    // Send the parameters a SECOND time, right after creation. TDX_Force firmware (app_effect.c):
                    // create_new_effect marks the slot RESERVED, and set_constant_force_report / set_periodic_report /
                    // set_ramp_report only store their values once the slot is DEFINED — which only the Set Effect
                    // report does. DirectInput's create-time download sends the type-specific report BEFORE Set
                    // Effect, so the initial magnitude is silently dropped and the effect plays at 0 until the value
                    // next changes (never, for a steady graph value). This is exactly what the working app's log
                    // shows: Set const force, Set eff, Set const force, Set eff, Start. (Condition effects don't
                    // have the rule, which is why springs/dampers always worked.) This also starts the effect.
                    effect.SetParameters(parameters, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Direction | EffectParameterFlags.Start);
                }
                catch
                {
                    effect.Dispose(); // don't leak a half-created effect
                    throw;
                }
                EffectsCreated++;
                _effects[node.Id] = new EffectState(effect, effectGuid, node.AxisIndex, node.Gain, node.DirectionDegrees, key);
            }
            else
            {
                var gainChanged = state.Gain != node.Gain;
                var directionChanged = targets.Count > 1 && Math.Abs(state.DirectionDegrees - node.DirectionDegrees) > 1e-9;

                // Talk to the device only when something actually changed: a call costs ~16 ms of USB
                // round trip (so ~60/s at most), and a graph whose value hasn't moved needs none at all.
                if (gainChanged || directionChanged || !key.AsSpan().SequenceEqual(state.Key))
                {
                    // Updates use the same pattern as the working app on this stick (TypeSpecific | Start, plus
                    // Direction so Set Effect is re-sent too — it carries the direction and gain, and is what
                    // the firmware's per-effect state hangs off): Set const force → Set eff → Start. The slot is
                    // already DEFINED by now, so the new magnitude is stored. Cost: Start restarts the effect
                    // (a periodic effect restarts its cycle when its parameters change) and a call takes ~16 ms.
                    var flags = EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start | EffectParameterFlags.Direction;
                    if (gainChanged) flags |= EffectParameterFlags.Gain;
                    if (effectGuid == EffectGuid.RampForce) flags |= EffectParameterFlags.Duration;

                    state.Effect.SetParameters(parameters, flags);
                    state.Gain = node.Gain;
                    state.DirectionDegrees = node.DirectionDegrees;
                    state.Key = key;
                }
            }

            applyReadout(node);
        }
        catch (Exception ex)
        {
            // One node's effect failing (driver rejects the parameters, device unplugged…) must not
            // throw out of the engine's timer callback — that would take the whole Runtime process down.
            SafeRemoveEffect(node.Id);
            _retryAt[node.Id] = Environment.TickCount64 + RetryDelayMs;
            EffectFailed?.Invoke(node, ex);
        }
    }

    /// <summary>The axes an effect drives: the whole set for <see cref="HardwareOutputNodeViewModel.AllAxes"/>, otherwise the chosen one.</summary>
    private static IReadOnlyList<FfbAxis> ResolveTargetAxes(IReadOnlyList<FfbAxis> axes, int axisIndex)
    {
        if (axisIndex == HardwareOutputNodeViewModel.AllAxes) return axes;
        return axisIndex >= 0 && axisIndex < axes.Count ? [axes[axisIndex]] : [];
    }

    /// <summary>
    /// Direction per effect axis (DirectInput's Cartesian form): a single axis needs none — the force's sign
    /// sets which way it pushes — and so does an effect type without a direction (conditions). Otherwise
    /// the dial's angle, placed by axis usage (see <see cref="EffectDirection"/>).
    /// </summary>
    private int[] BuildDirections(IReadOnlyList<FfbAxis> targets, HardwareOutputNodeViewModel node, Guid deviceGuid) =>
        targets.Count > 1 && node.SupportsDirection
            ? EffectDirection.ToVector(targets.Select(a => a.Usage).ToArray(), node.DirectionDegrees,
                _deviceManager.GetDirectionOffsetDegrees(deviceGuid))
            : new int[targets.Count];

    /// <summary>
    /// A comparable fingerprint of an effect's type-specific parameters *as DirectInput will see them*
    /// (already quantized to its integer units, so sub-unit jitter in the graph doesn't count as a change).
    /// </summary>
    private static long[] KeyOf(EffectParameters p) => p.Parameters switch
    {
        ConstantForce c => [c.Magnitude],
        PeriodicForce f => [f.Magnitude, f.Offset, f.Phase, f.Period],
        RampForce r => [r.Start, r.End, p.Duration],
        ConditionSet s => (s.Conditions ?? [])
            .SelectMany(c => new long[] { c.Offset, c.PositiveCoefficient, c.NegativeCoefficient, c.PositiveSaturation, c.NegativeSaturation, c.DeadBand })
            .ToArray(),
        _ => [],
    };

    // Continuous effects (everything except Ramp) play until explicitly stopped.
    private const int InfiniteDuration = int.MaxValue;

    private static EffectParameters BuildConstantForce(ConstantForceParams p) => new()
    {
        Duration = InfiniteDuration,
        Parameters = new ConstantForce { Magnitude = ToDirectInputUnits(p.Magnitude) },
    };

    // One condition per effect axis, in the effect's axis order. An axis the node drives gets the node's values
    // (the same on each, like the sibling TDX spring effect); one it doesn't gets an all-zero condition.
    private static EffectParameters BuildCondition(ConditionParams p, IReadOnlyList<bool> active) => new()
    {
        Duration = InfiniteDuration,
        Parameters = new ConditionSet
        {
            Conditions = active.Select(on => !on ? new Condition() : new Condition
            {
                Offset = ToDirectInputUnits(p.Offset),
                PositiveCoefficient = ToDirectInputUnits(p.PositiveCoefficient),
                NegativeCoefficient = ToDirectInputUnits(p.NegativeCoefficient),
                PositiveSaturation = ToDirectInputUnits(p.Saturation),
                NegativeSaturation = ToDirectInputUnits(p.Saturation),
                DeadBand = ToDirectInputUnits(p.DeadBand),
            }).ToArray(),
        },
    };

    private static EffectParameters BuildPeriodic(PeriodicParams p) => new()
    {
        Duration = InfiniteDuration,
        Parameters = new PeriodicForce
        {
            Magnitude = ToDirectInputUnits(p.Magnitude),
            Offset = ToDirectInputUnits(p.Offset),
            Phase = ToDirectInputPhase(p.Phase),
            Period = ToDirectInputMicroseconds(p.Period),
        },
    };

    private static EffectParameters BuildRamp(RampParams p) => new()
    {
        Duration = ToDirectInputMicroseconds(p.Duration),
        Parameters = new RampForce
        {
            Start = ToDirectInputUnits(p.StartMagnitude),
            End = ToDirectInputUnits(p.EndMagnitude),
        },
    };

    private static Guid ConditionEffectGuid(ConditionKind kind) => kind switch
    {
        ConditionKind.Spring => EffectGuid.Spring,
        ConditionKind.Damper => EffectGuid.Damper,
        ConditionKind.Inertia => EffectGuid.Inertia,
        ConditionKind.Friction => EffectGuid.Friction,
        _ => EffectGuid.Spring,
    };

    private static Guid WaveformEffectGuid(Waveform waveform) => waveform switch
    {
        Waveform.Sine => EffectGuid.Sine,
        Waveform.Square => EffectGuid.Square,
        Waveform.Triangle => EffectGuid.Triangle,
        Waveform.SawtoothUp => EffectGuid.SawtoothUp,
        Waveform.SawtoothDown => EffectGuid.SawtoothDown,
        _ => EffectGuid.Sine,
    };

    /// <summary>Normalized [-1, 1] (or [0, 1] for saturation/deadband) to DirectInput's fixed ±10000 range.</summary>
    private static int ToDirectInputUnits(double normalized) => (int)Math.Clamp(normalized * 10000, -10000, 10000);

    /// <summary>Seconds to DirectInput's microsecond duration/period units.</summary>
    private static int ToDirectInputMicroseconds(double seconds) => (int)Math.Clamp(seconds * 1_000_000, 0, int.MaxValue);

    /// <summary>Normalized [0, 1) cycle phase to DirectInput's hundredths-of-a-degree (0-35999).</summary>
    private static int ToDirectInputPhase(double normalized) => (int)(((normalized % 1.0 + 1.0) % 1.0) * 36000);

    private void RemoveEffect(Guid nodeId)
    {
        if (!_effects.Remove(nodeId, out var state)) return;
        state.Effect.Stop();
        state.Effect.Dispose();
    }

    // Teardown after a failure: the effect (or the device) may already be gone, so nothing here may throw.
    private void SafeRemoveEffect(Guid nodeId)
    {
        try { RemoveEffect(nodeId); }
        catch { _effects.Remove(nodeId); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var id in _effects.Keys.ToList())
                SafeRemoveEffect(id);
            _deviceManager.Dispose();
        }
    }
}
