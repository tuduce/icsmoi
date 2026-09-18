using System;
using System.IO;
using System.Text.Json;

namespace icsmoi.Runtime.ViewModels;

public sealed record RuntimeSettings(string? LastProfilePath = null);

/// <summary>Persists which profile the Runtime app should auto-load on next launch.</summary>
public sealed class RuntimeSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "icsmoi", "runtime-settings.json");

    public RuntimeSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new RuntimeSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<RuntimeSettings>(json) ?? new RuntimeSettings();
        }
        catch
        {
            return new RuntimeSettings();
        }
    }

    public void Save(RuntimeSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}
