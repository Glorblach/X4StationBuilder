using Newtonsoft.Json;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Core.Data;

/// <summary>
/// Catalogs the station modules available for blueprint assembly: which production module makes a
/// given ware, plus the storage, dock/pier, and habitat modules to choose from.
/// </summary>
/// <remarks>
/// Scanned module data written by the game-folder scanner (Step 03) is preferred when present so the
/// catalog reflects the user's installed DLCs; otherwise a bundled curated seed
/// (<c>Data/Maps/ModuleMap.json</c>, embedded) is used as a fallback.
/// </remarks>
public sealed class ModuleCatalog
{
    private const string SeedResource = "X4StationBuilder.Core.Data.Maps.ModuleMap.json";

    private readonly IReadOnlyList<StationModule> _modules;

    /// <summary>Creates a catalog over the supplied modules.</summary>
    public ModuleCatalog(IReadOnlyList<StationModule> modules)
    {
        _modules = modules;
    }

    /// <summary>All modules in the catalog.</summary>
    public IReadOnlyList<StationModule> Modules => _modules;

    /// <summary>Loads the catalog from the bundled embedded seed.</summary>
    public static ModuleCatalog FromBundledSeed() => new(LoadSeed());

    /// <summary>
    /// Creates a catalog, preferring scanned modules from <paramref name="store"/> when present and
    /// otherwise falling back to the bundled embedded seed.
    /// </summary>
    public static ModuleCatalog CreatePreferringScanned(ScannedDataStore store)
    {
        var scanned = store.LoadModules();
        return scanned is { Count: > 0 } ? new ModuleCatalog(scanned) : FromBundledSeed();
    }

    /// <summary>
    /// The production module that makes <paramref name="ware"/>, optionally restricted to
    /// <paramref name="faction"/>. Matches on internal ware id when available, else display name.
    /// </summary>
    public StationModule? GetProductionModule(Ware ware, string? faction = null) =>
        GetProductionModules(ware, faction).FirstOrDefault();

    /// <summary>All production modules that make <paramref name="ware"/>, optionally by faction.</summary>
    public IReadOnlyList<StationModule> GetProductionModules(Ware ware, string? faction = null)
    {
        return _modules
            .Where(m => m.Kind == ModuleKind.Production && ProducesWare(m, ware) && MatchesFaction(m, faction))
            .ToList();
    }

    /// <summary>The module with the given id, or null when not in the catalog.</summary>
    public StationModule? GetById(string? id) =>
        string.IsNullOrEmpty(id)
            ? null
            : _modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Storage modules, optionally filtered by cargo type and/or size.</summary>
    public IReadOnlyList<StationModule> GetStorageModules(
        CargoType? cargoType = null,
        ModuleSize? size = null)
    {
        return _modules
            .Where(m => m.Kind == ModuleKind.Storage)
            .Where(m => cargoType is null || m.CargoType == cargoType)
            .Where(m => size is null || m.Size == size)
            .ToList();
    }

    /// <summary>Dock modules (ship docking areas).</summary>
    public IReadOnlyList<StationModule> GetDockModules() =>
        _modules.Where(m => m.Kind == ModuleKind.Dock).ToList();

    /// <summary>Pier modules.</summary>
    public IReadOnlyList<StationModule> GetPierModules() =>
        _modules.Where(m => m.Kind == ModuleKind.Pier).ToList();

    /// <summary>
    /// Structural connector modules (e.g. <c>struct_*_cross/base/vertical</c>) used to frame a
    /// station. The layout engine picks a six-way "cross" connector from these.
    /// </summary>
    public IReadOnlyList<StationModule> GetConnectorModules() =>
        _modules.Where(m => m.Kind == ModuleKind.Connection).ToList();

    /// <summary>Habitat modules, optionally filtered by faction and/or size.</summary>
    public IReadOnlyList<StationModule> GetHabitatModules(
        string? faction = null,
        ModuleSize? size = null)
    {
        return _modules
            .Where(m => m.Kind == ModuleKind.Habitat)
            .Where(m => MatchesFaction(m, faction))
            .Where(m => size is null || m.Size == size)
            .ToList();
    }

    /// <summary>
    /// A display label for the production module that makes <paramref name="ware"/>: the module's
    /// localized name when known (e.g. "Energy Cell Production"), else a name derived from the ware.
    /// </summary>
    public string GetProductionModuleName(Ware ware, string? faction = null)
    {
        // "Common" recipes don't correspond to a module owner faction, so fall back to any module
        // that produces the ware when the faction-filtered lookup finds nothing.
        var module = GetProductionModule(ware, faction) ?? GetProductionModule(ware);
        if (!string.IsNullOrWhiteSpace(module?.Name))
        {
            return module!.Name!;
        }

        return $"{ware.Name} Production";
    }

    private static bool ProducesWare(StationModule module, Ware ware)
    {
        if (string.IsNullOrEmpty(module.ProducedWare))
        {
            return false;
        }

        if (ware.WareId is not null
            && string.Equals(module.ProducedWare, ware.WareId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(module.ProducedWare, ware.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFaction(StationModule module, string? faction)
    {
        if (string.IsNullOrEmpty(faction) || module.Factions.Count == 0)
        {
            return true;
        }

        return module.Factions.Any(f => string.Equals(f, faction, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<StationModule> LoadSeed()
    {
        var assembly = typeof(ModuleCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(SeedResource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{SeedResource}' not found. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<List<StationModule>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize ModuleMap.json.");
    }
}
