namespace X4StationBuilder.Core.Models;

/// <summary>
/// A production recipe in the map schema shared by the bundled JSON maps and scanned output.
/// </summary>
public sealed class RecipeData
{
    public int Amount { get; set; }
    public double WorkforceMultiplier { get; set; } = 1.0;
    public int WorkforceCapacity { get; set; }
    public Dictionary<string, double> Ingredients { get; set; } = new();
}

/// <summary>
/// Ware/recipe maps in the same structural schema as the bundled <c>Data/Maps</c> JSON files,
/// so <see cref="Data.WareRepository"/> can consume bundled or scanned data identically.
/// </summary>
public sealed class WareMaps
{
    /// <summary>Ware name → faction → recipe.</summary>
    public Dictionary<string, Dictionary<string, RecipeData>> RecipeMap { get; set; } = new();

    /// <summary>Ware name → default producing faction.</summary>
    public Dictionary<string, string> ItemFactionMap { get; set; } = new();

    /// <summary>Ware name → category.</summary>
    public Dictionary<string, string> ItemTypeMap { get; set; } = new();

    /// <summary>Ware display name → internal X4 ware id (e.g. "Energy cell" → "energycells").</summary>
    public Dictionary<string, string> WareIdMap { get; set; } = new();

    /// <summary>Ware name → cargo volume per unit in m³ (from <c>wares.xml</c>).</summary>
    public Dictionary<string, double> WareVolumeMap { get; set; } = new();

    /// <summary>Ware name → transport class ("container"/"solid"/"liquid").</summary>
    public Dictionary<string, string> WareTransportMap { get; set; } = new();

    /// <summary>
    /// Production-module macro → localized module name (e.g. <c>prod_gen_energycells_macro</c> →
    /// "Energy Cell Production"). Sourced from the buildable module wares; transient (not persisted).
    /// </summary>
    public Dictionary<string, string> ModuleNamesByMacro { get; set; } = new();
}

/// <summary>
/// The complete result of a game-folder scan: modules, ware maps, and metadata.
/// </summary>
public sealed class ScanResult
{
    public required ScanMetadata Metadata { get; init; }
    public IReadOnlyList<StationModule> Modules { get; init; } = [];
    public WareMaps Wares { get; init; } = new();
}
