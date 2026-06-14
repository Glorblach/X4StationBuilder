namespace X4StationBuilder.Core.Models;

/// <summary>
/// A single production recipe for a ware, as produced by a specific faction.
/// Mirrors the entries in <c>RecipeMap.json</c>.
/// </summary>
public sealed class Recipe
{
    /// <summary>Faction that produces the ware with this recipe (e.g. "Common", "Argon").</summary>
    public required string Faction { get; init; }

    /// <summary>Amount produced per production cycle.</summary>
    public int Amount { get; init; }

    /// <summary>Efficiency multiplier applied when the module is fully staffed.</summary>
    public double WorkforceMultiplier { get; init; }

    /// <summary>Number of workers the producing module can employ.</summary>
    public int WorkforceCapacity { get; init; }

    /// <summary>
    /// Input wares and their quantities per cycle. Keys are ware names; for the special
    /// "Workforce" pseudo-recipe the keys are <c>*</c>-prefixed and are preserved verbatim.
    /// </summary>
    public IReadOnlyDictionary<string, double> Ingredients { get; init; }
        = new Dictionary<string, double>();
}
