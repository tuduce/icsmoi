using System;
using System.Collections.Generic;
using System.Linq;
using icsmooi.Engine;
using icsmooi.Models;
using Vortice.DirectInput;

namespace icsmooi.Services.DirectInput;

/// <summary>
/// Keeps one long-lived DirectInput <see cref="IDirectInputEffect"/> per
/// hardware-output node, created once and updated in place every tick via
/// <c>SetParameters(..., EffectParameterFlags.TypeSpecificParameters)</c> —
/// never recreated per tick. Only <c>icsmooi.Runtime</c> instantiates this;
/// the Editor stays hardware-agnostic (see <see cref="FfbEngineService.HardwareOutputsUpdated"/>).
/// </summary>
public sealed class FfbEffectManager : IDisposable
{
    private readonly FfbDeviceManager _deviceManager;
    private readonly IntPtr _windowHandle;
    private readonly Dictionary<Guid, IDirectInputEffect> _effects = new();

    public FfbEffectManager(FfbDeviceManager deviceManager, IntPtr windowHandle)
    {
        _deviceManager = deviceManager;
        _windowHandle = windowHandle;
    }

    /// <summary>Call once per engine tick with the current node snapshot and this tick's computed effect parameters.</summary>
    public void SyncTick(IReadOnlyList<NodeViewModel> nodes, IReadOnlyDictionary<Guid, EffectOutputParams> effectParams)
    {
        var liveIds = new HashSet<Guid>();

        foreach (var node in nodes)
        {
            switch (node)
            {
                case ConstantForceOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } cf:
                    liveIds.Add(cf.Id);
                    if (effectParams.TryGetValue(cf.Id, out var cfRaw) && cfRaw is ConstantForceParams cfParams)
                        TryUpdate(cf.Id, deviceGuid, cf.AxisIndex, cf.Gain, () => BuildConstantForce(cfParams),
                            EffectGuid.ConstantForce, cf, n => n.LastMagnitude = cfParams.Magnitude);
                    break;

                case ConditionOutputNodeViewModel { DeviceInstanceGuid: { } deviceGuid } cond:
                    liveIds.Add(cond.Id);
                    if (effectParams.TryGetValue(cond.Id, out var condRaw) && condRaw is ConditionParams condParams)
                        TryUpdate(cond.Id, deviceGuid, cond.AxisIndex, cond.Gain, () => BuildCondition(condParams),
                            ConditionEffectGuid(cond.ConditionKind), cond, n => n.LastPositiveCoefficient = condParams.PositiveCoefficient);
                    break;
            }
        }

        // Node deleted, or lost its device assignment — stop and dispose its effect.
        foreach (var id in _effects.Keys.Except(liveIds).ToList())
            RemoveEffect(id);
    }

    private void TryUpdate<TNode>(
        Guid nodeId, Guid deviceGuid, int axisIndex, int gain,
        Func<EffectParameters> buildParams, Guid effectGuid, TNode node, Action<TNode> applyReadout)
    {
        var device = _deviceManager.AcquireDevice(deviceGuid, _windowHandle);
        var axes = _deviceManager.GetAxisOffsets(deviceGuid);
        if (axisIndex < 0 || axisIndex >= axes.Count) return;

        var parameters = buildParams();
        parameters.Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets;
        parameters.Gain = gain;
        parameters.Axes = [axes[axisIndex]];
        parameters.Directions = [0];

        if (!_effects.TryGetValue(nodeId, out var effect))
        {
            parameters.Duration = int.MaxValue;
            effect = device.CreateEffect(effectGuid, parameters);
            effect.Start(1);
            _effects[nodeId] = effect;
        }
        else
        {
            effect.SetParameters(parameters, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start);
        }

        applyReadout(node);
    }

    private static EffectParameters BuildConstantForce(ConstantForceParams p) => new()
    {
        Parameters = new ConstantForce { Magnitude = ToDirectInputUnits(p.Magnitude) },
    };

    private static EffectParameters BuildCondition(ConditionParams p) => new()
    {
        Parameters = new ConditionSet
        {
            Conditions =
            [
                new Condition
                {
                    Offset = ToDirectInputUnits(p.Offset),
                    PositiveCoefficient = ToDirectInputUnits(p.PositiveCoefficient),
                    NegativeCoefficient = ToDirectInputUnits(p.NegativeCoefficient),
                    PositiveSaturation = ToDirectInputUnits(p.Saturation),
                    NegativeSaturation = ToDirectInputUnits(p.Saturation),
                    DeadBand = ToDirectInputUnits(p.DeadBand),
                },
            ],
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

    /// <summary>Normalized [-1, 1] (or [0, 1] for saturation/deadband) to DirectInput's fixed ±10000 range.</summary>
    private static int ToDirectInputUnits(double normalized) => (int)Math.Clamp(normalized * 10000, -10000, 10000);

    private void RemoveEffect(Guid nodeId)
    {
        if (!_effects.Remove(nodeId, out var effect)) return;
        effect.Stop();
        effect.Dispose();
    }

    public void Dispose()
    {
        foreach (var id in _effects.Keys.ToList())
            RemoveEffect(id);
        _deviceManager.Dispose();
    }
}
