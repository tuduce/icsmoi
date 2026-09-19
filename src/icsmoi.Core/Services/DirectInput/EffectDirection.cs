using System;
using System.Collections.Generic;
using System.Linq;

namespace icsmoi.Services.DirectInput;

/// <summary>
/// Turns the direction dial's angle into the direction vector DirectInput wants for a multi-axis
/// effect. Pure (no device), so the geometry is testable without hardware.
///
/// <para>The dial's angle is where the force <b>pushes the stick</b>, in the pilot's frame: 0° is
/// forward (away from the pilot, "up" on the dial) and it increases clockwise — 90° pushes right,
/// 180° back, 270° left.</para>
/// </summary>
public static class EffectDirection
{
    private const int HidUsageX = 0x30;
    private const int HidUsageY = 0x31;
    private const double VectorScale = 10000;

    /// <summary>
    /// DirectInput documents an effect's direction as the direction the force comes <i>from</i> (a force
    /// from the north pushes the handle toward the pilot), so the pushed-toward angle is flipped 180°.
    /// This is the one place that assumption lives — <b>not yet confirmed on real hardware</b>; if a
    /// dial pointing forward pushes the stick back, set this to false.
    /// </summary>
    public const bool DirectInputDirectionIsSource = true;

    /// <summary>Wraps any angle into [0, 360).</summary>
    public static double Normalize(double degrees) => ((degrees % 360.0) + 360.0) % 360.0;

    /// <summary>
    /// The Cartesian direction vector (one component per effect axis, in the order of
    /// <paramref name="axisUsages"/>) for a force pushing toward <paramref name="pushDegrees"/>.
    /// Components are placed by each axis's HID usage (0x30 = X → left/right, 0x31 = Y → forward/back),
    /// <i>not</i> by list position: the TDX Force enumerates Y before X, so position would transpose the dial.
    /// Axes that are neither X nor Y get 0. <paramref name="deviceRotationDegrees"/> is added to the angle first, to
    /// compensate for a device whose firmware reads DirectInput's direction rotated (see
    /// <see cref="FfbDeviceManager.GetDirectionOffsetDegrees"/>).
    /// </summary>
    public static int[] ToVector(IReadOnlyList<int> axisUsages, double pushDegrees, double deviceRotationDegrees = 0)
    {
        var vector = new int[axisUsages.Count];
        if (vector.Length == 0) return vector;

        var radians = Normalize(pushDegrees + deviceRotationDegrees) * Math.PI / 180.0;
        // Pilot's frame: +X = right, and forward (dial 0°) is −Y (stick forward lowers Y).
        var pushX = Math.Sin(radians);
        var pushY = -Math.Cos(radians);
        if (DirectInputDirectionIsSource) { pushX = -pushX; pushY = -pushY; }

        var xIndex = IndexOfUsage(axisUsages, HidUsageX);
        var yIndex = IndexOfUsage(axisUsages, HidUsageY);
        // A device without recognizable X/Y usages: fall back to the first two axes in order.
        if (xIndex < 0) xIndex = FirstIndexOtherThan(vector.Length, yIndex);
        if (yIndex < 0) yIndex = FirstIndexOtherThan(vector.Length, xIndex);

        if (xIndex >= 0) vector[xIndex] = (int)Math.Round(pushX * VectorScale);
        if (yIndex >= 0) vector[yIndex] = (int)Math.Round(pushY * VectorScale);
        return vector;
    }

    private static int IndexOfUsage(IReadOnlyList<int> usages, int usage)
    {
        for (var i = 0; i < usages.Count; i++)
            if (usages[i] == usage) return i;
        return -1;
    }

    private static int FirstIndexOtherThan(int count, int excluded) =>
        Enumerable.Range(0, count).FirstOrDefault(i => i != excluded, -1);
}
