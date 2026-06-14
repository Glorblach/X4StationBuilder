using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services.Archive;
using X4StationBuilder.Core.Services.Parsing;

namespace X4StationBuilder.Core.Services;

/// <summary>Progress update emitted during a scan.</summary>
public readonly record struct ScanProgress(string Stage, double Fraction);

/// <summary>
/// Orchestrates a full game-folder scan: mounts the archives, detects DLCs, parses wares and
/// modules from the merged (DLC-aware) library XML, and produces a <see cref="ScanResult"/>.
/// </summary>
public sealed class GameFolderScanner
{
    private const string EnglishLanguageFile = "t/0001-l044.xml";
    private const string WaresLibrary = "libraries/wares.xml";
    private const string ModulesLibrary = "libraries/modules.xml";
    private const string ModuleGroupsLibrary = "libraries/modulegroups.xml";

    /// <summary>
    /// Scans <paramref name="gameRoot"/>. Throws <see cref="DirectoryNotFoundException"/> if the
    /// path is not a valid X4 install.
    /// </summary>
    public ScanResult Scan(string gameRoot, IProgress<ScanProgress>? progress = null)
    {
        if (!GameFolderLocator.IsValidGameFolder(gameRoot))
        {
            throw new DirectoryNotFoundException(
                $"'{gameRoot}' is not a valid X4 install (missing 01.cat or extensions folder).");
        }

        Report(progress, "Mounting archives", 0.05);
        var fs = X4FileSystem.Mount(gameRoot);

        Report(progress, "Detecting DLCs", 0.15);
        var dlcs = DlcDetector.Detect(gameRoot);

        Report(progress, "Loading localization", 0.30);
        var localization = LoadLocalization(fs);

        Report(progress, "Parsing wares", 0.55);
        var wareElements = XmlLibraryMerger.Merge(fs, WaresLibrary, "ware");
        var wares = WareParser.Parse(wareElements, localization);

        Report(progress, "Parsing modules", 0.80);
        var moduleElements = XmlLibraryMerger.Merge(fs, ModulesLibrary, "module");
        var groupElements = XmlLibraryMerger.Merge(fs, ModuleGroupsLibrary, "group");

        // First pass resolves macros; then read each module's workforce from its macro file and
        // re-parse so habitats (housing capacity) and production modules (workers employed) carry it,
        // along with the localized module name captured from the buildable module wares.
        var modules = ModuleParser.Parse(moduleElements, groupElements, null, wares.ModuleNamesByMacro);
        var usedMacros = modules.Where(m => m.Macro is not null).Select(m => m.Macro!).ToList();
        var macroWorkforce = MacroWorkforceReader.Read(fs, usedMacros);

        Report(progress, "Parsing module geometry", 0.88);
        var macroGeometry = MacroGeometryReader.Read(fs, usedMacros);

        var storageMacros = modules
            .Where(m => m.Kind == ModuleKind.Storage && m.Macro is not null)
            .Select(m => m.Macro!)
            .ToList();
        var macroCargo = MacroCargoReader.Read(fs, storageMacros);

        if (macroWorkforce.Count > 0 || macroGeometry.Count > 0 || macroCargo.Count > 0)
        {
            modules = ModuleParser.Parse(
                moduleElements, groupElements, macroWorkforce, wares.ModuleNamesByMacro,
                macroGeometry, macroCargo);
        }

        // Fold each production module's employed-workforce onto the recipes for the ware it makes,
        // so the calculator can apply the workforce multiplier and size habitats.
        ApplyProductionWorkforce(wares, modules);

        Report(progress, "Finalising", 0.95);
        var result = new ScanResult
        {
            Modules = modules,
            Wares = wares,
            Metadata = new ScanMetadata
            {
                GamePath = gameRoot,
                Dlcs = dlcs,
                ModuleCount = modules.Count,
                WareCount = wares.RecipeMap.Count,
            },
        };

        Report(progress, "Done", 1.0);
        return result;
    }

    /// <summary>
    /// Copies each production module's employed workforce onto the recipes of the ware it produces.
    /// Modules reference wares by internal id; recipes are keyed by display name, so this reverses
    /// the ware id→name map to join them. Faction recipes without a capacity get the module's value
    /// (the larger value wins when several modules make the same ware).
    /// </summary>
    private static void ApplyProductionWorkforce(WareMaps wares, IReadOnlyList<StationModule> modules)
    {
        var idToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, id) in wares.WareIdMap)
        {
            idToName[id] = name;
        }

        foreach (var module in modules)
        {
            if (module.Kind != ModuleKind.Production
                || module.WorkforceCapacity <= 0
                || string.IsNullOrEmpty(module.ProducedWare))
            {
                continue;
            }

            if (!idToName.TryGetValue(module.ProducedWare, out var wareName)
                || !wares.RecipeMap.TryGetValue(wareName, out var factionRecipes))
            {
                continue;
            }

            foreach (var recipe in factionRecipes.Values)
            {
                if (module.WorkforceCapacity > recipe.WorkforceCapacity)
                {
                    recipe.WorkforceCapacity = module.WorkforceCapacity;
                }
            }
        }
    }

    private static LocalizationTable LoadLocalization(X4FileSystem fs)
    {
        var table = new LocalizationTable();
        foreach (var entry in fs.GetAll(EnglishLanguageFile))
        {
            try
            {
                table.LoadXml(CatDatReader.ReadText(entry));
            }
            catch (IOException)
            {
                // Skip unreadable language overlays.
            }
        }

        return table;
    }

    private static void Report(IProgress<ScanProgress>? progress, string stage, double fraction) =>
        progress?.Report(new ScanProgress(stage, fraction));
}
