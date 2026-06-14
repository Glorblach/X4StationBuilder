using Newtonsoft.Json;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Reads and writes scanned game data as JSON in a folder next to the running executable
/// (<c>&lt;AppContext.BaseDirectory&gt;\ScannedData\</c>), falling back to a per-user app-data folder
/// when the executable directory is not writable. Never writes into the game folder or source tree.
/// </summary>
public sealed class ScannedDataStore
{
    public const string ModulesFile = "modules.json";
    public const string RecipeMapFile = "RecipeMap.json";
    public const string ItemFactionMapFile = "ItemFactionMap.json";
    public const string ItemTypeMapFile = "ItemTypeMap.json";
    public const string WareIdMapFile = "WareIdMap.json";
    public const string WareVolumeMapFile = "WareVolumeMap.json";
    public const string WareTransportMapFile = "WareTransportMap.json";
    public const string MetadataFile = "scan-metadata.json";

    private readonly string _dataDir;

    public ScannedDataStore(string? dataDir = null)
    {
        _dataDir = dataDir ?? ResolveDataDirectory();
    }

    /// <summary>Directory the store reads from / writes to.</summary>
    public string DataDirectory => _dataDir;

    /// <summary>True if a previous scan's data is present.</summary>
    public bool HasScannedData =>
        File.Exists(Path.Combine(_dataDir, RecipeMapFile))
        || File.Exists(Path.Combine(_dataDir, ModulesFile));

    /// <summary>
    /// Picks the output directory: <c>&lt;exe dir&gt;\ScannedData</c> if writable, otherwise
    /// <c>%LocalAppData%\X4StationBuilder\ScannedData</c>.
    /// </summary>
    public static string ResolveDataDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        var preferred = Path.Combine(exeDir, "ScannedData");
        if (IsDirectoryWritable(exeDir))
        {
            return preferred;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "X4StationBuilder",
            "ScannedData");
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes the full scan result as the four data files plus metadata.</summary>
    public void Save(ScanResult result)
    {
        Directory.CreateDirectory(_dataDir);

        WriteJson(ModulesFile, result.Modules);
        WriteJson(RecipeMapFile, result.Wares.RecipeMap);
        WriteJson(ItemFactionMapFile, result.Wares.ItemFactionMap);
        WriteJson(ItemTypeMapFile, result.Wares.ItemTypeMap);
        WriteJson(WareIdMapFile, result.Wares.WareIdMap);
        WriteJson(WareVolumeMapFile, result.Wares.WareVolumeMap);
        WriteJson(WareTransportMapFile, result.Wares.WareTransportMap);
        WriteJson(MetadataFile, result.Metadata);
    }

    /// <summary>Loads scanned ware maps if present, otherwise null.</summary>
    public WareMaps? LoadWareMaps()
    {
        var recipe = ReadJson<Dictionary<string, Dictionary<string, RecipeData>>>(RecipeMapFile);
        if (recipe is null)
        {
            return null;
        }

        return new WareMaps
        {
            RecipeMap = recipe,
            ItemFactionMap = ReadJson<Dictionary<string, string>>(ItemFactionMapFile) ?? new(),
            ItemTypeMap = ReadJson<Dictionary<string, string>>(ItemTypeMapFile) ?? new(),
            WareIdMap = ReadJson<Dictionary<string, string>>(WareIdMapFile) ?? new(),
            WareVolumeMap = ReadJson<Dictionary<string, double>>(WareVolumeMapFile) ?? new(),
            WareTransportMap = ReadJson<Dictionary<string, string>>(WareTransportMapFile) ?? new(),
        };
    }

    /// <summary>Loads scanned modules if present, otherwise null.</summary>
    public IReadOnlyList<StationModule>? LoadModules() =>
        ReadJson<List<StationModule>>(ModulesFile);

    /// <summary>Loads scan metadata if present, otherwise null.</summary>
    public ScanMetadata? LoadMetadata() =>
        ReadJson<ScanMetadata>(MetadataFile);

    private void WriteJson(string fileName, object value)
    {
        var json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(Path.Combine(_dataDir, fileName), json);
    }

    private T? ReadJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(_dataDir, fileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
