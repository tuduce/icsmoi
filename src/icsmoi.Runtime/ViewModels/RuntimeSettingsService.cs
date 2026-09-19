using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace icsmoi.Runtime.ViewModels;

/// <summary>
/// One remembered "this aircraft uses that profile". Keyed by the profile's file path (not its name, which
/// the Editor can change); <see cref="Name"/> is only the last-known display name for the warning message.
/// </summary>
public sealed record AircraftAssociation(string Path, string Name);

public sealed class RuntimeSettings
{
    /// <summary>How many profiles the Runtime holds at once.</summary>
    public const int SlotCount = 4;

    /// <summary>Profile file path per slot; <c>null</c> = empty slot. Always <see cref="SlotCount"/> long once loaded.</summary>
    public string?[] Slots { get; set; } = new string?[SlotCount];

    /// <summary>The slot whose profile the engine runs.</summary>
    public int? ActiveSlot { get; set; }

    /// <summary>Aircraft <c>TITLE</c> → the profile last chosen for it (case-insensitive once loaded).</summary>
    public Dictionary<string, AircraftAssociation> AircraftProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Single-profile setting from before slots existed — read for migration only, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastProfilePath { get; set; }
}

/// <summary>Persists the Runtime's slots, active slot and aircraft → profile associations.</summary>
public sealed class RuntimeSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "icsmoi", "runtime-settings.json");

    public RuntimeSettings Load()
    {
        var settings = new RuntimeSettings();

        if (File.Exists(SettingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<RuntimeSettings>(File.ReadAllText(SettingsPath)) ?? settings;
            }
            catch
            {
                // Unreadable settings are the same as none: start with empty slots.
            }
        }

        // Normalise: the JSON may hold any number of slots, and the deserialised dictionary has the
        // default (case-sensitive) comparer.
        var slots = new string?[RuntimeSettings.SlotCount];
        for (var i = 0; i < slots.Length && settings.Slots is not null && i < settings.Slots.Length; i++)
            slots[i] = string.IsNullOrWhiteSpace(settings.Slots[i]) ? null : settings.Slots[i];
        settings.Slots = slots;

        settings.AircraftProfiles = new Dictionary<string, AircraftAssociation>(
            settings.AircraftProfiles ?? [], StringComparer.OrdinalIgnoreCase);

        if (settings.ActiveSlot is < 0 or >= RuntimeSettings.SlotCount)
            settings.ActiveSlot = null;

        // Migration: the old single "last profile" becomes slot 1 and the active profile.
        if (settings.LastProfilePath is { } legacy)
        {
            if (Array.TrueForAll(settings.Slots, s => s is null))
            {
                settings.Slots[0] = legacy;
                settings.ActiveSlot = 0;
            }
            settings.LastProfilePath = null;
        }

        return settings;
    }

    public void Save(RuntimeSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
