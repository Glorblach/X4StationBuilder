using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// A ware the user wants produced, with a target output rate in items/hour, an optional explicit
/// faction choice, and an optional specific production module to place (e.g. the Terran variant).
/// </summary>
public sealed record DesiredWare(
    string WareName,
    double ItemsPerHour,
    string? Faction = null,
    string? PreferredModuleId = null);

/// <summary>
/// Options controlling a production-chain calculation, including the workforce hook.
/// </summary>
public sealed record ProductionOptions
{
    /// <summary>
    /// When true, staffed modules apply their <see cref="Recipe.WorkforceMultiplier"/> (fewer
    /// modules needed), workers are counted, habitats are added, and the workforce's food + medical
    /// consumption is expanded into additional production modules.
    /// </summary>
    public bool WorkforceEnabled { get; init; }

    /// <summary>
    /// Faction whose workforce food/medical needs are used (food differs by faction; e.g. Argon eats
    /// food rations, Teladi nostrop oil). Falls back to a known workforce faction if unavailable.
    /// </summary>
    public string WorkforceFaction { get; init; } = "Argon";

    /// <summary>
    /// When true (and <see cref="WorkforceEnabled"/>), the workforce's food and medical supplies are
    /// produced on-station: their production modules (and sub-chains) are added and sized to feed the
    /// workers. When false, those supplies are assumed to be imported and instead appear as raw
    /// resources to bring in from elsewhere. Has no effect when <see cref="WorkforceEnabled"/> is false.
    /// </summary>
    public bool ProduceWorkforceSupplies { get; init; } = true;

    /// <summary>
    /// Optional ware name → faction overrides applied throughout the tree (case-insensitive).
    /// A per-<see cref="DesiredWare"/> faction takes precedence over this map for that ware.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FactionOverrides { get; init; }

    /// <summary>
    /// Extra workers employed by non-production modules that are part of the station but not part of
    /// the production chain — chiefly build modules (wharf/shipyard ship fabrication bays). When
    /// <see cref="WorkforceEnabled"/>, these are added to the worker total so habitats are sized and
    /// the workforce's food/medical demand is produced for them too. Ignored when workforce is off.
    /// </summary>
    public int ExtraWorkforce { get; init; }

    /// <summary>
    /// Optional station species/faction (e.g. "Terran"). When set, every produced ware that has a
    /// module variant for this faction uses that variant by default (e.g. a Terran build gets the
    /// Terran Energy Cell Production module rather than the generic one), unless the user pinned a
    /// specific module for that ware. Only affects which module is placed/displayed, not the recipe
    /// quantities. Null/empty leaves module selection at the per-ware default.
    /// </summary>
    public string? PreferredModuleFaction { get; init; }
}

/// <summary>
/// The result of a production-chain calculation: the modules required to satisfy the desired
/// output, the raw resources consumed, and any problems encountered.
/// </summary>
public sealed class ProductionResult
{
    /// <summary>
    /// All production modules required (desired wares plus every producible intermediate, including
    /// the workforce's food/medical chains when workforce is enabled), with duplicate wares merged.
    /// Ordered by category sort index then name.
    /// </summary>
    public required IReadOnlyList<FactoryGroup> RequiredFactoryGroups { get; init; }

    /// <summary>
    /// Total raw (non-producible) resources consumed, ware name → items/hour. These are ingredients
    /// with no recipe (ores, gases, etc.).
    /// </summary>
    public required IReadOnlyDictionary<string, double> TotalRawResources { get; init; }

    /// <summary>
    /// Desired ware names that could not be produced (unknown ware, or a ware with no recipe).
    /// </summary>
    public required IReadOnlyList<string> UnproducibleDesiredWares { get; init; }

    /// <summary>Ware names where a production cycle was detected and expansion was halted.</summary>
    public required IReadOnlyList<string> CyclicWares { get; init; }

    /// <summary>True when this result was computed with the workforce hook enabled.</summary>
    public bool WorkforceEnabled { get; init; }

    /// <summary>Total workers employed across all staffed modules (0 when workforce is disabled).</summary>
    public int TotalWorkers { get; init; }

    /// <summary>Habitat modules required to house <see cref="TotalWorkers"/> (empty when disabled).</summary>
    public IReadOnlyList<HabitatRequirement> Habitats { get; init; } = [];
}

/// <summary>
/// Recursively resolves the production modules and raw resources required to produce a set of
/// desired wares at target rates. Ported from the reference planner's recursive expansion.
/// </summary>
/// <remarks>
/// When workforce is enabled (see <see cref="ProductionOptions.WorkforceEnabled"/>), staffed modules
/// apply their <see cref="Recipe.WorkforceMultiplier"/>, the worker count is summed, the workforce's
/// food/medical consumption is added as additional desired production (resolved to a fixed point,
/// since those chains themselves employ workforce), and habitats are sized to house the workers.
/// Faction selection per ware is: explicit override → the ware's default faction → the first
/// available recipe.
/// </remarks>
public sealed class ProductionCalculator
{
    private const int MaxDepth = 64;
    private const int MaxWorkforceIterations = 64;
    private const string WorkforceWareName = "Workforce";

    private static readonly string[] FallbackWorkforceFactions = ["Argon", "Paranid", "Teladi"];

    /// <summary>
    /// Per-worker hourly workforce consumption used when scanned data has no "Workforce" pseudo-recipe
    /// (the bundled maps carry one; live game data does not). Keyed by workforce faction → the food and
    /// medical ware ids consumed by that race, with rates per worker per hour. Values approximate the
    /// x4-game.com station calculator's workforce demand.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string FoodWareId, string MedicalWareId, double FoodPerWorker)>
        WorkforceFoodBasket = new Dictionary<string, (string, string, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Argon"] = ("foodrations", "medicalsupplies", 2.25),
            ["Paranid"] = ("sojahusk", "medicalsupplies", 2.25),
            ["Teladi"] = ("nostropoil", "medicalsupplies", 2.25),
            ["Terran"] = ("terranmre", "medicalsupplies", 2.25),
            ["Boron"] = ("bofu", "medicalsupplies", 2.25),
        };

    private const double MedicalPerWorker = 1.35;

    private readonly WareRepository _wares;
    private readonly ModuleCatalog? _modules;

    /// <summary>
    /// Creates a calculator. Supply a <paramref name="modules"/> catalog to enable habitat selection
    /// when the workforce hook is used; without it the worker count is still reported but no habitats
    /// are produced.
    /// </summary>
    public ProductionCalculator(WareRepository wares, ModuleCatalog? modules = null)
    {
        _wares = wares ?? throw new ArgumentNullException(nameof(wares));
        _modules = modules;
    }

    /// <summary>
    /// Computes the required production modules and raw resources for the given desired wares.
    /// </summary>
    /// <param name="desired">Desired wares with target output rates (items/hour).</param>
    /// <param name="factionOverrides">
    /// Optional ware name → faction overrides applied throughout the tree (case-insensitive).
    /// A per-<see cref="DesiredWare"/> faction takes precedence over this map for that ware.
    /// </param>
    public ProductionResult Calculate(
        IEnumerable<DesiredWare> desired,
        IReadOnlyDictionary<string, string>? factionOverrides = null)
    {
        return Calculate(desired, new ProductionOptions { FactionOverrides = factionOverrides });
    }

    /// <summary>
    /// Computes the required production modules and raw resources for the given desired wares using
    /// the supplied <paramref name="options"/> (including the optional workforce hook).
    /// </summary>
    public ProductionResult Calculate(IEnumerable<DesiredWare> desired, ProductionOptions options)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(options);

        var wants = desired.Where(w => w.ItemsPerHour > 0).ToList();

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.FactionOverrides is not null)
        {
            foreach (var (ware, faction) in options.FactionOverrides)
            {
                overrides[ware] = faction;
            }
        }

        // Per-desired-ware faction wins over the general override map.
        foreach (var want in wants)
        {
            if (want.Faction is not null)
            {
                overrides[want.WareName] = want.Faction;
            }
        }

        var workforceFaction = ResolveWorkforceFaction(options.WorkforceFaction);
        var produceSupplies = options.WorkforceEnabled && options.ProduceWorkforceSupplies;

        // Map each desired ware to a specific production module the user chose (e.g. the Terran
        // variant), so layout/export can place that module rather than the default for the ware.
        var preferredModules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var want in wants)
        {
            if (!string.IsNullOrWhiteSpace(want.PreferredModuleId))
            {
                preferredModules[want.WareName] = want.PreferredModuleId!;
            }
        }

        // Resolve the workforce demand (workers → food/medical) to a fixed point: workforce
        // food/medical chains themselves employ workers, which increases the demand again. (Only when
        // supplies are produced on-station; otherwise the food/medical chains aren't added.)
        // Build modules (and any other non-production modules) contribute a fixed extra worker count
        // that joins the production workforce in sizing food/medical demand and habitats.
        var extraWorkforce = options.WorkforceEnabled ? Math.Max(0, options.ExtraWorkforce) : 0;
        var workers = extraWorkforce;
        var pass = RunPass(wants, overrides, options.WorkforceEnabled, produceSupplies, workforceFaction, workers, preferredModules, options.PreferredModuleFaction);
        var newWorkers = ComputeWorkers(pass, options.WorkforceEnabled) + extraWorkforce;

        var guard = 0;
        while (newWorkers != workers && guard++ < MaxWorkforceIterations)
        {
            workers = newWorkers;
            pass = RunPass(wants, overrides, options.WorkforceEnabled, produceSupplies, workforceFaction, workers, preferredModules, options.PreferredModuleFaction);
            newWorkers = ComputeWorkers(pass, options.WorkforceEnabled) + extraWorkforce;
        }

        var totalWorkers = newWorkers;

        var ordered = pass.Groups
            .OrderBy(g => _wares.CategorySortIndex(g.Category))
            .ThenBy(g => g.WareName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var habitats = options.WorkforceEnabled && totalWorkers > 0
            ? SelectHabitats(workforceFaction, totalWorkers)
            : [];

        var rawTotals = pass.RawTotals;

        // When supplies are imported (not produced locally), surface the workforce's food/medical
        // demand as raw resources to bring in from an external factory.
        if (options.WorkforceEnabled && !options.ProduceWorkforceSupplies && totalWorkers > 0)
        {
            var imports = new Dictionary<string, double>(rawTotals, StringComparer.OrdinalIgnoreCase);
            foreach (var (ware, demand) in WorkforceConsumption(workforceFaction, totalWorkers))
            {
                Accumulate(imports, ware.Name, demand);
            }

            rawTotals = imports;
        }

        return new ProductionResult
        {
            RequiredFactoryGroups = ordered,
            TotalRawResources = rawTotals,
            UnproducibleDesiredWares = pass.Unproducible,
            CyclicWares = pass.Cyclic,
            WorkforceEnabled = options.WorkforceEnabled,
            TotalWorkers = totalWorkers,
            Habitats = habitats,
        };
    }

    /// <summary>One full expansion pass for the desired wares plus a fixed workforce demand.</summary>
    private ExpansionPass RunPass(
        IReadOnlyList<DesiredWare> wants,
        Dictionary<string, string> overrides,
        bool workforceEnabled,
        bool produceSupplies,
        string workforceFaction,
        int workersDemand,
        IReadOnlyDictionary<string, string>? preferredModules = null,
        string? preferredModuleFaction = null)
    {
        var producedTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var rawTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var chosenFaction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unproducible = new List<string>();
        var cyclic = new List<string>();
        var cyclicSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var want in wants)
        {
            var ware = _wares.GetByName(want.WareName);
            if (ware is null || !ware.IsProducible)
            {
                unproducible.Add(want.WareName);
                continue;
            }

            Expand(
                want.WareName,
                want.ItemsPerHour,
                overrides,
                workforceEnabled,
                producedTotals,
                rawTotals,
                chosenFaction,
                cyclic,
                cyclicSet,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                depth: 0,
                preferredModuleFaction);
        }

        if (workforceEnabled && produceSupplies && workersDemand > 0)
        {
            foreach (var (ware, demand) in WorkforceConsumption(workforceFaction, workersDemand))
            {
                if (demand <= 0)
                {
                    continue;
                }

                Expand(
                    ware.Name,
                    demand,
                    overrides,
                    workforceEnabled: true,
                    producedTotals,
                    rawTotals,
                    chosenFaction,
                    cyclic,
                    cyclicSet,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    depth: 0,
                    preferredModuleFaction);
            }
        }

        var groups = new List<FactoryGroup>();
        foreach (var (wareName, itemCount) in producedTotals)
        {
            var ware = _wares.GetByName(wareName)!;
            var faction = chosenFaction[wareName];
            var recipe = ware.RecipesByFaction[faction];

            // User-pinned module wins; otherwise default to the station species' module variant
            // when one exists for this ware (e.g. the Terran Energy Cell Production module).
            var preferredModuleId = preferredModules?.GetValueOrDefault(wareName)
                                    ?? SpeciesModuleId(ware, preferredModuleFaction);

            groups.Add(new FactoryGroup
            {
                Ware = ware,
                Faction = faction,
                Recipe = recipe,
                ItemCount = itemCount,
                WorkforceStaffed = workforceEnabled && recipe.WorkforceCapacity > 0,
                PreferredModuleId = preferredModuleId,
            });
        }

        return new ExpansionPass(groups, rawTotals, unproducible, cyclic);
    }

    /// <summary>
    /// The id of the production module that makes <paramref name="ware"/> for
    /// <paramref name="faction"/> (the station species), or null when no catalog/faction is given or
    /// no such variant exists. Used to default intermediates and workforce supplies to the species'
    /// own module rather than the generic one.
    /// </summary>
    private string? SpeciesModuleId(Ware ware, string? faction)
    {
        if (_modules is null || string.IsNullOrWhiteSpace(faction))
        {
            return null;
        }

        return _modules.GetProductionModule(ware, faction)?.Id;
    }

    /// <summary>
    /// The workforce's food/medical demand for a worker count, as (ware, items/hour) pairs. Uses the
    /// "Workforce" pseudo-recipe when present (bundled maps); otherwise falls back to a built-in
    /// per-worker basket resolved against scanned ware ids (live game data has no such recipe).
    /// </summary>
    private IEnumerable<(Ware Ware, double Demand)> WorkforceConsumption(
        string workforceFaction,
        int workersDemand)
    {
        if (workersDemand <= 0)
        {
            yield break;
        }

        var workforceWare = _wares.GetByName(WorkforceWareName);
        if (workforceWare is not null
            && workforceWare.RecipesByFaction.TryGetValue(workforceFaction, out var recipe))
        {
            var stationCount = recipe.Amount > 0 ? (double)workersDemand / recipe.Amount : workersDemand;

            foreach (var (ingredient, perStation) in recipe.Ingredients)
            {
                var ingredientName = ingredient.TrimStart('*');

                // The pseudo-recipe lists Workforce itself as an ingredient; skip the self-reference.
                if (string.Equals(ingredientName, WorkforceWareName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ware = _wares.GetByName(ingredientName);
                if (ware is not null)
                {
                    yield return (ware, perStation * stationCount);
                }
            }

            yield break;
        }

        // No pseudo-recipe (scanned data): use the built-in per-worker basket resolved by ware id.
        if (!WorkforceFoodBasket.TryGetValue(workforceFaction, out var basket))
        {
            basket = WorkforceFoodBasket["Argon"];
        }

        var food = _wares.GetByWareId(basket.FoodWareId);
        if (food is not null)
        {
            yield return (food, basket.FoodPerWorker * workersDemand);
        }

        var medical = _wares.GetByWareId(basket.MedicalWareId);
        if (medical is not null)
        {
            yield return (medical, MedicalPerWorker * workersDemand);
        }
    }

    private void Expand(
        string wareName,
        double itemsPerHour,
        Dictionary<string, string> overrides,
        bool workforceEnabled,
        Dictionary<string, double> producedTotals,
        Dictionary<string, double> rawTotals,
        Dictionary<string, string> chosenFaction,
        List<string> cyclic,
        HashSet<string> cyclicSet,
        HashSet<string> path,
        int depth,
        string? preferredFaction)
    {
        var ware = _wares.GetByName(wareName);

        // Non-producible ingredient → raw resource.
        if (ware is null || !ware.IsProducible)
        {
            Accumulate(rawTotals, wareName, itemsPerHour);
            return;
        }

        if (!path.Add(wareName) || depth >= MaxDepth)
        {
            // Cycle (or runaway depth): count this ware's demand but stop expanding it.
            if (cyclicSet.Add(wareName))
            {
                cyclic.Add(wareName);
            }

            Accumulate(producedTotals, wareName, itemsPerHour);
            EnsureFaction(ware, overrides, chosenFaction, preferredFaction);
            return;
        }

        try
        {
            Accumulate(producedTotals, wareName, itemsPerHour);

            var faction = EnsureFaction(ware, overrides, chosenFaction, preferredFaction);
            var recipe = ware.RecipesByFaction[faction];
            var effectiveAmount = EffectiveAmount(recipe, workforceEnabled);
            var stationCount = effectiveAmount > 0 ? itemsPerHour / effectiveAmount : 0;

            foreach (var (ingredient, perStation) in recipe.Ingredients)
            {
                // Strip the '*' marker used by the workforce pseudo-recipe ingredients.
                var ingredientName = ingredient.TrimStart('*');
                var demand = perStation * stationCount;
                if (demand <= 0)
                {
                    continue;
                }

                Expand(
                    ingredientName,
                    demand,
                    overrides,
                    workforceEnabled,
                    producedTotals,
                    rawTotals,
                    chosenFaction,
                    cyclic,
                    cyclicSet,
                    path,
                    depth + 1,
                    preferredFaction);
            }
        }
        finally
        {
            path.Remove(wareName);
        }
    }

    /// <summary>Sums the workers employed across all staffed groups in a pass.</summary>
    private static int ComputeWorkers(ExpansionPass pass, bool workforceEnabled)
    {
        if (!workforceEnabled)
        {
            return 0;
        }

        return pass.Groups.Sum(g => g.Workers);
    }

    /// <summary>Per-module hourly output, boosted by the workforce multiplier when staffed.</summary>
    private static double EffectiveAmount(Recipe recipe, bool workforceEnabled) =>
        workforceEnabled && recipe.WorkforceCapacity > 0
            ? recipe.Amount * recipe.WorkforceMultiplier
            : recipe.Amount;

    /// <summary>Picks the most worker-efficient habitats (largest capacity) to house the workers.</summary>
    private IReadOnlyList<HabitatRequirement> SelectHabitats(string workforceFaction, int workers)
    {
        if (_modules is null)
        {
            return [];
        }

        // Prefer faction-matched habitats; fall back to any habitat if the faction has none.
        var candidates = _modules.GetHabitatModules(workforceFaction)
            .Where(m => m.WorkforceCapacity > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = _modules.GetHabitatModules()
                .Where(m => m.WorkforceCapacity > 0)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        // Largest-capacity habitat minimises module count for a single-type selection.
        var habitat = candidates.OrderByDescending(m => m.WorkforceCapacity).First();
        var count = (int)Math.Ceiling((double)workers / habitat.WorkforceCapacity);
        return [new HabitatRequirement(habitat, count)];
    }

    private string ResolveWorkforceFaction(string? requested)
    {
        var workforceWare = _wares.GetByName(WorkforceWareName);

        // Respect the user's chosen species when we can model its food demand — either via the
        // "Workforce" pseudo-recipe (bundled data) or the built-in per-worker basket (scanned data).
        if (!string.IsNullOrWhiteSpace(requested)
            && (workforceWare?.RecipesByFaction.ContainsKey(requested) == true
                || WorkforceFoodBasket.ContainsKey(requested)))
        {
            return requested;
        }

        if (workforceWare is null)
        {
            return string.IsNullOrWhiteSpace(requested) ? FallbackWorkforceFactions[0] : requested;
        }

        foreach (var fallback in FallbackWorkforceFactions)
        {
            if (workforceWare.RecipesByFaction.ContainsKey(fallback))
            {
                return fallback;
            }
        }

        return workforceWare.RecipesByFaction.Keys.FirstOrDefault()
            ?? (string.IsNullOrWhiteSpace(requested) ? FallbackWorkforceFactions[0] : requested);
    }

    private static string EnsureFaction(
        Ware ware,
        Dictionary<string, string> overrides,
        Dictionary<string, string> chosenFaction,
        string? preferredFaction)
    {
        if (chosenFaction.TryGetValue(ware.Name, out var existing))
        {
            return existing;
        }

        var faction = ResolveFaction(ware, overrides, preferredFaction);
        chosenFaction[ware.Name] = faction;
        return faction;
    }

    private static string ResolveFaction(Ware ware, Dictionary<string, string> overrides, string? preferredFaction)
    {
        if (overrides.TryGetValue(ware.Name, out var requested)
            && ware.RecipesByFaction.ContainsKey(requested))
        {
            return requested;
        }

        // Station species recipe (e.g. the Terran medical-supplies recipe) when this ware has one,
        // so a Terran build uses Terran ingredients instead of the Commonwealth default.
        if (!string.IsNullOrWhiteSpace(preferredFaction)
            && ware.RecipesByFaction.ContainsKey(preferredFaction))
        {
            return preferredFaction;
        }

        if (ware.DefaultFaction is not null
            && ware.RecipesByFaction.ContainsKey(ware.DefaultFaction))
        {
            return ware.DefaultFaction;
        }

        // Fall back to the first available recipe's faction (DefaultRecipe is non-null here
        // because callers only expand producible wares).
        return ware.RecipesByFaction.Keys.First();
    }

    private static void Accumulate(Dictionary<string, double> totals, string key, double value)
    {
        totals[key] = totals.TryGetValue(key, out var current) ? current + value : value;
    }

    /// <summary>The accumulated results of one expansion pass.</summary>
    private sealed record ExpansionPass(
        IReadOnlyList<FactoryGroup> Groups,
        IReadOnlyDictionary<string, double> RawTotals,
        IReadOnlyList<string> Unproducible,
        IReadOnlyList<string> Cyclic);
}
