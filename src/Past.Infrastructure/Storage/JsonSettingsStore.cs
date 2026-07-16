using System.Text.Json;
using Past.Services;

namespace Past.Infrastructure.Storage;

/// <summary>
/// Stores preferences as plain JSON next to the database. Deliberately not encrypted:
/// these are non-sensitive toggles, and a human-readable file is easier to inspect —
/// which suits a privacy-first app. Clip contents never go here.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public JsonSettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings must never block startup — fall back to defaults.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
