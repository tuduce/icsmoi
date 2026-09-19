using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using icsmoi.Models;

namespace icsmoi.Services;

public sealed class ProfileSerializerService
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // Polymorphic serialization is handled via [JsonDerivedType] on NodeViewModel
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public async Task SaveAsync(FfbProfile profile, string filePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write beside the target, then swap it in: the Runtime polls saved profiles and hot-reloads
        // them, so it must never see a half-written file.
        var tempPath = filePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, profile, _options);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* the original error matters more */ }
            throw;
        }
    }

    public async Task<FfbProfile> LoadAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Profile file not found.", filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var profile = await JsonSerializer.DeserializeAsync<FfbProfile>(stream, _options);
        return profile ?? throw new InvalidOperationException("Deserialized profile was null.");
    }
}
