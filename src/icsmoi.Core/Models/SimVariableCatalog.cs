using System.Collections.Generic;
using System.Linq;

namespace icsmoi.Models;

public readonly record struct SimVariableDefinition(string Name, string Units);

/// <summary>
/// Seed catalog of MSFS SimConnect variables relevant to FFB effects.
/// Used both by <see cref="SimConnectNodeViewModel"/>'s variable picker and by
/// <c>SimConnectTelemetryService</c>'s data-definition registration — this
/// list's order must match that service's <c>TelemetryData</c> struct field
/// order exactly, since SimConnect maps registered variables to struct fields
/// positionally, not by name.
/// </summary>
public static class SimVariableCatalog
{
    public const int Count = 16;

    public static readonly IReadOnlyList<SimVariableDefinition> KnownVariables = new[]
    {
        new SimVariableDefinition("AIRSPEED INDICATED", "knots"),
        new SimVariableDefinition("AIRSPEED BARBER POLE", "knots"),
        new SimVariableDefinition("GENERAL ENG THROTTLE LEVER POSITION:1", "percent"),
        new SimVariableDefinition("STALL WARNING", "Boolean"),
        new SimVariableDefinition("SIM ON GROUND", "Boolean"),
        new SimVariableDefinition("SURFACE TYPE", "Enum"),
        new SimVariableDefinition("GROUND VELOCITY", "meters per second"),
        new SimVariableDefinition("GEAR HANDLE POSITION", "Boolean"),
        new SimVariableDefinition("G FORCE", "GForce"),
        new SimVariableDefinition("AILERON POSITION", "position"),
        new SimVariableDefinition("ELEVATOR POSITION", "position"),
        new SimVariableDefinition("RUDDER POSITION", "position"),
        new SimVariableDefinition("PLANE BANK DEGREES", "degrees"),
        new SimVariableDefinition("PLANE PITCH DEGREES", "degrees"),
        new SimVariableDefinition("PLANE ALT ABOVE GROUND", "feet"),
        new SimVariableDefinition("FLAPS HANDLE PERCENT", "percent"),
    };

    public static readonly IReadOnlyList<string> VariableNames =
        KnownVariables.Select(v => v.Name).ToArray();

    static SimVariableCatalog()
    {
        System.Diagnostics.Debug.Assert(KnownVariables.Count == Count,
            $"{nameof(Count)} ({Count}) must match {nameof(KnownVariables)}.Count ({KnownVariables.Count}).");
    }
}
