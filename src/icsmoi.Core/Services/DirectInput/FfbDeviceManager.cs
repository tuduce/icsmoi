using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.DirectInput;

namespace icsmoi.Services.DirectInput;

/// <summary>One force-feedback axis of a device: its display name, its offset in the joystick data format, and its HID usage (0x30 = X, 0x31 = Y).</summary>
public sealed record FfbAxis(string Name, int Offset, int Usage);

public sealed record FfbDeviceInfo(Guid InstanceGuid, string ProductName, IReadOnlyList<FfbAxis> Axes);

/// <summary>
/// Enumerates and acquires DirectInput force-feedback-capable devices, and
/// caches each acquired device's FFB axis map — discovered once at
/// acquisition time rather than re-run by every effect (a deliberate fix
/// versus TDX-Air-Mechanics, where each effect class independently re-runs
/// axis discovery).
/// </summary>
public sealed class FfbDeviceManager : IDisposable
{
    private const int HidUsageX = 0x30;
    private const int HidUsageY = 0x31;

    // How long to let a freshly reset stick work through its queued reports (once per device, on first acquire).
    private const int InitSettleMilliseconds = 500;

    private readonly IDirectInput8 _directInput = DInput.DirectInput8Create();
    private readonly Dictionary<Guid, IDirectInputDevice8> _acquiredDevices = new();
    private readonly Dictionary<Guid, IReadOnlyList<FfbAxis>> _axes = new();

    /// <summary>
    /// Lists attached devices that expose at least one force-feedback actuator, with their FFB axes.
    /// Safe in the Editor: axis discovery only *creates* the device object to read its object list —
    /// no cooperative level, no <c>Acquire</c>, no effects (that is <see cref="AcquireDevice"/>, Runtime-only).
    /// </summary>
    public IReadOnlyList<FfbDeviceInfo> EnumerateDevices()
    {
        return _directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly | DeviceEnumerationFlags.ForceFeedback)
            .Select(d => new FfbDeviceInfo(d.InstanceGuid, d.ProductName, ReadAxes(d.InstanceGuid)))
            .ToList();
    }

    private IReadOnlyList<FfbAxis> ReadAxes(Guid instanceGuid)
    {
        try
        {
            using var device = _directInput.CreateDevice(instanceGuid);
            return DiscoverAxes(device);
        }
        catch
        {
            return []; // a device that can't be inspected just offers "All axes" in the picker
        }
    }

    /// <summary>
    /// The axes a device can push on, in a stable order (by HID usage — X, then Y, …): the *axis* objects
    /// flagged as force-feedback actuators. Enumerating with <c>DeviceObjectTypeFlags.ForceFeedbackActuator</c>
    /// directly finds nothing on some devices (the TDX Force reports none that way, and lists Y before X),
    /// so we filter the axis list ourselves, falling back to the X/Y usages when a driver omits the actuator flag.
    /// </summary>
    internal static IReadOnlyList<FfbAxis> DiscoverAxes(IDirectInputDevice8 device)
    {
        var axes = device.GetObjects(DeviceObjectTypeFlags.Axis);
        var actuators = axes.Where(a => a.ObjectId.Flags.HasFlag(DeviceObjectTypeFlags.ForceFeedbackActuator)).ToList();
        if (actuators.Count == 0)
            actuators = axes.Where(a => a.Usage is HidUsageX or HidUsageY).ToList();

        return actuators
            .OrderBy(a => a.Usage)
            .ThenBy(a => a.Offset)
            .Select(a => new FfbAxis(a.Name, a.Offset, a.Usage))
            .ToList();
    }

    /// <summary>
    /// Acquires a device exclusively (required for FFB) in the background so
    /// forces keep applying while the host app isn't focused, and caches its
    /// FFB axes. Returns the same instance on repeated calls for the
    /// same GUID. Throws if the device can't be acquired.
    /// </summary>
    public IDirectInputDevice8 AcquireDevice(Guid instanceGuid, IntPtr windowHandle)
    {
        if (_acquiredDevices.TryGetValue(instanceGuid, out var existing))
            return existing;

        var device = _directInput.CreateDevice(instanceGuid);
        try
        {
            // Without a data format Acquire() fails with E_INVALIDARG (verified against a real device) —
            // the SharpDX/Vortice Joystick class the sibling TDX projects use sets it implicitly.
            device.SetDataFormat<RawJoystickState>().CheckError();
            device.SetCooperativeLevel(windowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background).CheckError();

            // Same setup as the working sibling app (TDX-Air-Mechanics MechanicService). Best-effort: a device
            // that refuses one of these properties can still play effects.
            try { device.Properties.AutoCenter = false; } catch { }
            try { device.Properties.ForceFeedbackGain = 10000; } catch { }

            device.Acquire().CheckError();

            // Initialise the stick NOW, explicitly, and let it finish before anything is created. If we don't,
            // DirectInput does its own reset lazily inside the first CreateEffect — and on the TDX Force that
            // reset is an output report queued behind a slow UART-logging thread while Create New Effect is a
            // control transfer handled immediately, so the reset ran *after* the effect was created and
            // wiped it (TDX_Force firmware: reset_effects() sets every slot IDLE, and Set Effect / Set Constant
            // Force ignore an IDLE slot) — no force, no error. The working app's log shows exactly this
            // order: Stop-all, gain, Reset, and only then Create New Effect.
            try { device.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll); } catch { }
            try { device.SendForceFeedbackCommand(ForceFeedbackCommand.Reset); } catch { }
            System.Threading.Thread.Sleep(InitSettleMilliseconds);

            _axes[instanceGuid] = DiscoverAxes(device);
        }
        catch
        {
            device.Dispose();
            throw;
        }

        _acquiredDevices[instanceGuid] = device;
        return device;
    }

    /// <summary>The cached FFB axes for a previously acquired device.</summary>
    public IReadOnlyList<FfbAxis> GetAxes(Guid instanceGuid) =>
        _axes.TryGetValue(instanceGuid, out var axes) ? axes : [];

    /// <summary>
    /// Degrees to add to a direction before it is handed to DirectInput, for devices whose firmware reads it
    /// rotated. The TDX Force applies a constant force as <c>roll = −F·cos(dir)</c>, <c>pitch = −F·sin(dir)</c>
    /// (TDX_Force/Core/Src/app_ffb.c, <c>FFB_ComputeConstantForce</c>) — a math-convention angle, whereas
    /// DirectInput's polar direction is clockwise from north. Net effect measured on the stick: it pushes 90°
    /// clockwise of where asked (forward-right came out right-and-back), so we ask for 90° less. Other devices: 0.
    /// </summary>
    public double GetDirectionOffsetDegrees(Guid instanceGuid)
    {
        if (!_acquiredDevices.TryGetValue(instanceGuid, out var device)) return 0;
        var name = device.DeviceInfo?.ProductName ?? "";
        return name.Contains("TDX Force", StringComparison.OrdinalIgnoreCase) ? -90 : 0;
    }

    public void ReleaseDevice(Guid instanceGuid)
    {
        if (!_acquiredDevices.Remove(instanceGuid, out var device)) return;

        device.Unacquire();
        device.Dispose();
        _axes.Remove(instanceGuid);
    }

    public void Dispose()
    {
        foreach (var guid in _acquiredDevices.Keys.ToList())
            ReleaseDevice(guid);

        _directInput.Dispose();
    }
}
