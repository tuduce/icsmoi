using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using icsmoi.Models;
using Vortice.DirectInput;

namespace icsmoi.Services.DirectInput;

public sealed record InputDeviceInfo(Guid InstanceGuid, string ProductName);

/// <summary>An axis or button, as detected by <see cref="JoystickDevices.Capture"/>.</summary>
public readonly record struct JoystickInput(JoystickInputKind Kind, int Index);

/// <summary>
/// Read-only access to DirectInput game controllers for the Joystick Input node. Unlike
/// <see cref="FfbDeviceManager"/> this is for *any* joystick (force feedback not required) and
/// only ever reads state, so it is safe in the Editor as well as the Runtime: devices are opened
/// non-exclusively in the background and never get an effect.
/// </summary>
public static class JoystickDevices
{
    // Every axis is forced to this symmetric range so normalization is uniform across devices
    // (DirectInput's default varies by driver).
    private const int AxisRange = 1000;

    /// <summary>Lists attached game controllers (joysticks, throttles, pedals, wheels, gamepads…).</summary>
    public static IReadOnlyList<InputDeviceInfo> EnumerateDevices()
    {
        using var directInput = DInput.DirectInput8Create();
        return directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .Select(d => new InputDeviceInfo(d.InstanceGuid, d.ProductName))
            .ToList();
    }

    /// <summary>
    /// Opens a device for reading. Throws if it can't be acquired (unplugged, or another app holds it
    /// exclusively); the caller owns the returned device.
    /// </summary>
    internal static IDirectInputDevice8 Open(IDirectInput8 directInput, Guid instanceGuid, IntPtr windowHandle)
    {
        var device = directInput.CreateDevice(instanceGuid);
        try
        {
            device.SetDataFormat<RawJoystickState>().CheckError();
            device.SetCooperativeLevel(windowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background).CheckError();
            // Must be set before Acquire. A device without axes may refuse it — buttons still work.
            try { device.Properties.Range = new InputRange(-AxisRange, AxisRange); } catch { }
            device.Acquire().CheckError();
            return device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the current state: axes normalized to [-1, 1] (indexed by <see cref="JoystickAxis"/>) and
    /// button states. Throws if the device has been lost.
    /// </summary>
    internal static void Read(IDirectInputDevice8 device, double[] axes, bool[] buttons)
    {
        if (device.Poll().Failure)
            device.Acquire(); // input lost (e.g. the sim grabbed it) — try to get it back

        var state = device.GetCurrentJoystickState();

        axes[(int)JoystickAxis.X] = Normalize(state.X);
        axes[(int)JoystickAxis.Y] = Normalize(state.Y);
        axes[(int)JoystickAxis.Z] = Normalize(state.Z);
        axes[(int)JoystickAxis.RotationX] = Normalize(state.RotationX);
        axes[(int)JoystickAxis.RotationY] = Normalize(state.RotationY);
        axes[(int)JoystickAxis.RotationZ] = Normalize(state.RotationZ);
        axes[(int)JoystickAxis.Slider0] = Normalize(state.Sliders[0]);
        axes[(int)JoystickAxis.Slider1] = Normalize(state.Sliders[1]);

        Array.Copy(state.Buttons, buttons, Math.Min(state.Buttons.Length, buttons.Length));
    }

    private static double Normalize(int raw) => Math.Clamp(raw / (double)AxisRange, -1.0, 1.0);

    /// <summary>
    /// Blocks until the user presses a button or moves an axis/slider on the device, and returns which.
    /// Returns null if <paramref name="cancellation"/> fires first. Buttons already held when the capture
    /// starts are ignored until released and pressed again; axes are measured against their starting position.
    /// Run this off the UI thread.
    /// </summary>
    public static JoystickInput? Capture(Guid instanceGuid, IntPtr windowHandle, CancellationToken cancellation)
    {
        using var directInput = DInput.DirectInput8Create();
        using var device = Open(directInput, instanceGuid, windowHandle);

        var axes = new double[JoystickInputKey.AxisCount];
        var buttons = new bool[JoystickInputKey.ButtonCount];

        // The first state read after Acquire is stale (all zeros), which would make every button
        // that is already held look freshly pressed — let the device settle before taking the baseline.
        Read(device, axes, buttons);
        cancellation.WaitHandle.WaitOne(80);
        Read(device, axes, buttons);
        var detector = new JoystickCaptureDetector(axes, buttons);

        while (!cancellation.IsCancellationRequested)
        {
            cancellation.WaitHandle.WaitOne(15);
            Read(device, axes, buttons);
            if (detector.Update(axes, buttons) is { } input)
                return input;
        }

        return null;
    }
}

/// <summary>
/// Decides which input the user actuated, from successive samples of a joystick. Pure (no device),
/// so the rules are unit-testable: a button counts when it goes from released to pressed (one that
/// was already held is ignored until released and pressed again), an axis counts once it has moved
/// a quarter of its full travel from where it started (so sensor noise and a resting hand don't
/// trigger), and when several qualify in the same sample a button wins, else the axis that moved most.
/// </summary>
public sealed class JoystickCaptureDetector
{
    // Fraction of an axis's -1..1 span (2.0) it must move from its starting position: 0.25 = 12.5% of travel.
    public const double AxisMoveThreshold = 0.25;

    private readonly double[] _startAxes;
    private readonly bool[] _previousButtons;

    public JoystickCaptureDetector(double[] startAxes, bool[] startButtons)
    {
        _startAxes = (double[])startAxes.Clone();
        _previousButtons = (bool[])startButtons.Clone();
    }

    /// <summary>Feed the next sample; returns the actuated input once there is one.</summary>
    public JoystickInput? Update(double[] axes, bool[] buttons)
    {
        var pressed = -1;
        for (var i = 0; i < buttons.Length && i < _previousButtons.Length; i++)
        {
            if (buttons[i] && !_previousButtons[i] && pressed < 0) pressed = i;
            _previousButtons[i] = buttons[i];
        }
        if (pressed >= 0)
            return new JoystickInput(JoystickInputKind.Button, pressed);

        var bestAxis = -1;
        var bestDelta = AxisMoveThreshold;
        for (var i = 0; i < axes.Length && i < _startAxes.Length; i++)
        {
            var delta = Math.Abs(axes[i] - _startAxes[i]);
            if (delta >= bestDelta) { bestDelta = delta; bestAxis = i; }
        }

        return bestAxis >= 0 ? new JoystickInput(JoystickInputKind.Axis, bestAxis) : null;
    }
}
