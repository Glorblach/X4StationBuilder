using Newtonsoft.Json;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// A user-configured default docking entry: a dock/pier module identified by its macro and how many
/// of it to seed into a new plan. Stored by macro (the stable module identity) and resolved against
/// the current <c>ModuleCatalog</c> at seed time; entries whose macro isn't currently available are
/// skipped gracefully (e.g. when no game scan is present).
/// </summary>
public sealed class DockDefault
{
    /// <summary>Macro of the dock/pier module (e.g. <c>dockarea_arg_m_station_01_macro</c>).</summary>
    public string Macro { get; set; } = string.Empty;

    /// <summary>How many of this dock/pier to include by default.</summary>
    public int Count { get; set; }
}

/// <summary>
/// Persisted application settings.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Last validated X4 install path, or null if never chosen.</summary>
    public string? GamePath { get; set; }

    /// <summary>
    /// Optional prefix prepended to the auto-generated station/plan name (e.g. <c>NML-ARG</c>), used
    /// for sorting blueprints in-game. Applied only to the generated default name; manual edits are
    /// preserved.
    /// </summary>
    public string? StationNamePrefix { get; set; }

    /// <summary>
    /// The user's default set of docking modules (by macro + count) seeded into each new plan.
    /// </summary>
    public List<DockDefault> DefaultDocks { get; set; } = [];
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as a JSON file in the per-user app-data folder
/// (<c>%LocalAppData%\X4StationBuilder\settings.json</c>).
/// </summary>
public sealed class SettingsStore
{
    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath();
    }

    /// <summary>Absolute path of the settings file.</summary>
    public string SettingsPath => _settingsPath;

    public static string DefaultSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "X4StationBuilder");
        return Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Fall through to defaults on any read/parse failure.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(_settingsPath, json);
    }
}
