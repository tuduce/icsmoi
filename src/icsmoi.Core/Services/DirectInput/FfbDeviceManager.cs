using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.DirectInput;

namespace icsmoi.Services.DirectInput;

public sealed record FfbDeviceInfo(Guid InstanceGuid, string ProductName);

/// <summary>
/// Enumerates and acquires DirectInput force-feedback-capable devices, and
/// caches each acquired device's FFB axis map — discovered once at
/// acquisition time rather than re-run by every effect (a deliberate fix
/// versus TDX-Air-Mechanics, where each effect class independently re-runs
/// axis discovery).
/// </summary>
public sealed class FfbDeviceManager : IDisposable
{
    private readonly IDirectInput8 _directInput = DInput.DirectInput8Create();
    private readonly Dictionary<Guid, IDirectInputDevice8> _acquiredDevices = new();
    private readonly Dictionary<Guid, int[]> _axisOffsets = new();

    /// <summary>Lists attached devices that expose at least one force-feedback actuator.</summary>
    public IReadOnlyList<FfbDeviceInfo> EnumerateDevices()
    {
        return _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly | DeviceEnumerationFlags.ForceFeedback)
            .Select(d => new FfbDeviceInfo(d.InstanceGuid, d.ProductName))
            .ToList();
    }

    /// <summary>
    /// Acquires a device exclusively (required for FFB) in the background so
    /// forces keep applying while the host app isn't focused, and caches its
    /// FFB axis offsets. Returns the same instance on repeated calls for the
    /// same GUID.
    /// </summary>
    public IDirectInputDevice8 AcquireDevice(Guid instanceGuid, IntPtr windowHandle)
    {
        if (_acquiredDevices.TryGetValue(instanceGuid, out var existing))
            return existing;

        var device = _directInput.CreateDevice(instanceGuid);
        device.SetCooperativeLevel(windowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background);
        device.Acquire();

        _acquiredDevices[instanceGuid] = device;
        _axisOffsets[instanceGuid] = device.GetObjects(DeviceObjectTypeFlags.ForceFeedbackActuator)
            .Select(o => o.Offset)
            .ToArray();

        return device;
    }

    /// <summary>The cached FFB-capable axis offsets for a previously acquired device.</summary>
    public IReadOnlyList<int> GetAxisOffsets(Guid instanceGuid) =>
        _axisOffsets.TryGetValue(instanceGuid, out var offsets) ? offsets : [];

    public void ReleaseDevice(Guid instanceGuid)
    {
        if (!_acquiredDevices.Remove(instanceGuid, out var device)) return;

        device.Unacquire();
        device.Dispose();
        _axisOffsets.Remove(instanceGuid);
    }

    public void Dispose()
    {
        foreach (var guid in _acquiredDevices.Keys.ToList())
            ReleaseDevice(guid);

        _directInput.Dispose();
    }
}
