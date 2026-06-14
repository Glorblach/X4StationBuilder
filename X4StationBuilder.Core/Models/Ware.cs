namespace X4StationBuilder.Core.Models;

/// <summary>
/// A station ware: its category, default producing faction, and the per-faction recipes
/// that produce it. Leaf resources (e.g. Hydrogen, Ore) may have no recipes but still
/// exist as lookupable wares because they appear as ingredients.
/// </summary>
public sealed class Ware
{
    /// <summary>Display name of the ware (e.g. "Antimatter cell"). Acts as the unique key.</summary>
    public required string Name { get; init; }

    /// <summary>Internal X4 ware id (e.g. "energycells"), or null when loaded from bundled maps.</summary>
    public string? WareId { get; init; }

    /// <summary>Category from <c>ItemTypeMap.json</c> (e.g. "Energy", "RefinedResource"), or null if unknown.</summary>
    public string? Category { get; init; }

    /// <summary>Cargo volume per unit in m³ (from <c>wares.xml</c> <c>volume</c>); 0 when unknown.</summary>
    public double Volume { get; init; }

    /// <summary>Transport class ("container"/"solid"/"liquid") from <c>wares.xml</c>; null when unknown.</summary>
    public string? TransportType { get; init; }

    /// <summary>Default producing faction from <c>ItemFactionMap.json</c>, or null for leaf resources.</summary>
    public string? DefaultFaction { get; init; }

    /// <summary>Recipes keyed by faction. Empty for leaf resources that are never produced on-station.</summary>
    public IReadOnlyDictionary<string, Recipe> RecipesByFaction { get; init; }
        = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Factions able to produce this ware (the faction keys of its recipes).</summary>
    public IReadOnlyList<string> ProducibleByFactions { get; init; } = [];

    /// <summary>True if this ware has at least one production recipe.</summary>
    public bool IsProducible => RecipesByFaction.Count > 0;

    /// <summary>The recipe for the default faction, or the first available recipe, or null.</summary>
    public Recipe? DefaultRecipe
    {
        get
        {
            if (DefaultFaction is not null && RecipesByFaction.TryGetValue(DefaultFaction, out var r))
            {
                return r;
            }

            foreach (var recipe in RecipesByFaction.Values)
            {
                return recipe;
            }

            return null;
        }
    }
}
