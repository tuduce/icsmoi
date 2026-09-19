using System;
using System.Collections.Generic;

namespace icsmoi.Runtime.ViewModels;

public enum AircraftDecisionKind
{
    /// <summary>No aircraft, or nothing remembered for it: leave the profile choice alone.</summary>
    None,

    /// <summary>Its remembered profile is loaded in a slot: activate that slot.</summary>
    Switch,

    /// <summary>Its remembered profile is not loaded in any slot: warn, and leave the profile choice alone.</summary>
    WarnMissing,
}

public readonly record struct AircraftDecision(AircraftDecisionKind Kind, int SlotIndex = -1, AircraftAssociation? Association = null);

/// <summary>
/// The plane → profile rule, kept free of UI and SimConnect so it can be exercised on its own.
/// </summary>
public static class AircraftProfileResolver
{
    /// <param name="title">The aircraft's SimConnect <c>TITLE</c>, or <c>null</c> when none is loaded.</param>
    /// <param name="associations">Remembered aircraft → profile choices.</param>
    /// <param name="loadedSlotPaths">Per slot, the profile file path if a profile is actually loaded there, else <c>null</c>.</param>
    public static AircraftDecision Resolve(
        string? title,
        IReadOnlyDictionary<string, AircraftAssociation> associations,
        IReadOnlyList<string?> loadedSlotPaths)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) return new AircraftDecision(AircraftDecisionKind.None);

        var match = FindAssociation(associations, title);
        if (match is null) return new AircraftDecision(AircraftDecisionKind.None);

        for (var i = 0; i < loadedSlotPaths.Count; i++)
            if (loadedSlotPaths[i] is { } path && SamePath(path, match.Path))
                return new AircraftDecision(AircraftDecisionKind.Switch, i, match);

        return new AircraftDecision(AircraftDecisionKind.WarnMissing, Association: match);
    }

    // Looked up directly first (the Runtime's own dictionary is case-insensitive), then scanned, so a
    // caller passing a plain dictionary gets the same forgiving match.
    private static AircraftAssociation? FindAssociation(IReadOnlyDictionary<string, AircraftAssociation> associations, string title)
    {
        if (associations.TryGetValue(title, out var direct)) return direct;
        foreach (var (key, value) in associations)
            if (string.Equals(key.Trim(), title, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path); }
        catch { return path; }
    }
}
