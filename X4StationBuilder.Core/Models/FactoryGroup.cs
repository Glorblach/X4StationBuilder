namespace X4StationBuilder.Core.Models;

/// <summary>
/// A resolved production requirement: a ware, the faction/recipe chosen to produce it, and how
/// many modules ("stations") are needed to meet a desired output rate.
/// </summary>
/// <remarks>
/// Rates are in items/hour. <see cref="EffectiveAmount"/> is the per-module hourly output, so a
/// fractional <see cref="StationCount"/> is simply <see cref="ItemCount"/> / <c>EffectiveAmount</c>.
/// When <see cref="WorkforceStaffed"/> is true the recipe's <see cref="Recipe.WorkforceMultiplier"/>
/// boosts that output (fewer modules needed) and the module employs <see cref="Recipe.WorkforceCapacity"/>
/// workers.
/// </remarks>
public sealed class FactoryGroup
{
    /// <summary>The ware this group produces.</summary>
    public required Ware Ware { get; init; }

    /// <summary>The faction whose recipe was chosen to produce the ware.</summary>
    public required string Faction { get; init; }

    /// <summary>
    /// Optional id of a specific production module the user chose to place for this ware (e.g. the
    /// Terran variant). When set, layout/export uses this module instead of the default for the ware.
    /// Only set for top-level desired wares; null for auto-resolved dependencies.
    /// </summary>
    public string? PreferredModuleId { get; init; }

    /// <summary>The recipe used to produce the ware (the <see cref="Faction"/>'s recipe).</summary>
    public required Recipe Recipe { get; init; }

    /// <summary>Required output rate in items/hour.</summary>
    public required double ItemCount { get; init; }

    /// <summary>
    /// True when the module is staffed: its <see cref="Recipe.WorkforceMultiplier"/> boosts output
    /// and it employs <see cref="Recipe.WorkforceCapacity"/> workers. Set only when workforce is
    /// enabled and the recipe employs workers.
    /// </summary>
    public bool WorkforceStaffed { get; init; }

    /// <summary>Display name of the produced ware.</summary>
    public string WareName => Ware.Name;

    /// <summary>Category of the produced ware (from the ware data), or null if unknown.</summary>
    public string? Category => Ware.Category;

    /// <summary>
    /// Per-module hourly output. Equals <see cref="Recipe.Amount"/>, boosted by
    /// <see cref="Recipe.WorkforceMultiplier"/> when <see cref="WorkforceStaffed"/> is true.
    /// </summary>
    public double EffectiveAmount =>
        WorkforceStaffed ? Recipe.Amount * Recipe.WorkforceMultiplier : Recipe.Amount;

    /// <summary>Fractional number of modules required (<see cref="ItemCount"/> / effective output).</summary>
    public double StationCount => EffectiveAmount > 0 ? ItemCount / EffectiveAmount : 0;

    /// <summary>Whole number of modules required, rounded up.</summary>
    public int StationCountCeil => (int)Math.Ceiling(StationCount);

    /// <summary>Workers employed by all modules in this group (0 unless <see cref="WorkforceStaffed"/>).</summary>
    public int Workers => WorkforceStaffed ? StationCountCeil * Recipe.WorkforceCapacity : 0;
}
