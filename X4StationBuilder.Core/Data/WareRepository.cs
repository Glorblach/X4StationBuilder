using Newtonsoft.Json;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Core.Data;

/// <summary>
/// Loads X4 ware and recipe data and exposes it as strongly-typed <see cref="Ware"/> objects.
/// </summary>
/// <remarks>
/// Scanned data written by the game-folder scanner (Step 03) is preferred when present so the
/// catalog reflects the user's installed DLCs; otherwise the bundled JSON maps (embedded resources
/// under <c>Data/Maps/</c>) are used as a fallback.
/// </remarks>
public sealed class WareRepository
{
    private const string ResourcePrefix = "X4StationBuilder.Core.Data.Maps.";

    private readonly Dictionary<string, Ware> _waresByName;
    private readonly Dictionary<string, Ware> _waresById = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<Ware> _allWares;
    private readonly IReadOnlyDictionary<string, int> _categorySortOrder;

    /// <summary>Loads from the bundled embedded JSON maps.</summary>
    public WareRepository()
        : this(
            LoadMap<Dictionary<string, Dictionary<string, RecipeDto>>>("RecipeMap.json"),
            LoadMap<Dictionary<string, string>>("ItemFactionMap.json"),
            LoadMap<Dictionary<string, string>>("ItemTypeMap.json"))
    {
    }

    /// <summary>
    /// Creates a repository, preferring scanned data from <paramref name="store"/> when present and
    /// otherwise falling back to the bundled embedded maps.
    /// </summary>
    public static WareRepository CreatePreferringScanned(ScannedDataStore store)
    {
        var scanned = store.LoadWareMaps();
        return scanned is not null ? FromWareMaps(scanned) : new WareRepository();
    }

    /// <summary>Builds a repository from in-memory <see cref="WareMaps"/> (e.g. a fresh scan).</summary>
    public static WareRepository FromWareMaps(WareMaps maps)
    {
        var recipeMap = maps.RecipeMap.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToDictionary(
                inner => inner.Key,
                inner => new RecipeDto
                {
                    Amount = inner.Value.Amount,
                    WorkforceMultiplier = inner.Value.WorkforceMultiplier,
                    WorkforceCapacity = inner.Value.WorkforceCapacity,
                    Ingredients = new Dictionary<string, double>(inner.Value.Ingredients),
                },
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        return new WareRepository(
            recipeMap,
            new Dictionary<string, string>(maps.ItemFactionMap, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(maps.ItemTypeMap, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(maps.WareIdMap, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, double>(maps.WareVolumeMap, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(maps.WareTransportMap, StringComparer.OrdinalIgnoreCase));
    }

    private WareRepository(
        Dictionary<string, Dictionary<string, RecipeDto>> recipeMap,
        Dictionary<string, string> itemFactionMap,
        Dictionary<string, string> itemTypeMap,
        Dictionary<string, string>? wareIdMap = null,
        Dictionary<string, double>? wareVolumeMap = null,
        Dictionary<string, string>? wareTransportMap = null)
    {
        var itemTypeSortMap = LoadMap<Dictionary<string, int>>("ItemTypeSortMap.json");

        _categorySortOrder = itemTypeSortMap;

        // Collect every ware name that appears anywhere, including ingredient-only leaf resources.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in recipeMap.Keys) names.Add(name);
        foreach (var name in itemFactionMap.Keys) names.Add(name);
        foreach (var name in itemTypeMap.Keys) names.Add(name);
        foreach (var factions in recipeMap.Values)
        {
            foreach (var recipe in factions.Values)
            {
                if (recipe.Ingredients is null) continue;
                foreach (var ingredient in recipe.Ingredients.Keys)
                {
                    // Strip the '*' marker used by the Workforce pseudo-recipe ingredients.
                    names.Add(ingredient.TrimStart('*'));
                }
            }
        }

        _waresByName = new Dictionary<string, Ware>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var recipesByFaction = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
            if (recipeMap.TryGetValue(name, out var factionRecipes))
            {
                foreach (var (faction, dto) in factionRecipes)
                {
                    recipesByFaction[faction] = new Recipe
                    {
                        Faction = faction,
                        Amount = dto.Amount,
                        WorkforceMultiplier = dto.WorkforceMultiplier,
                        WorkforceCapacity = dto.WorkforceCapacity,
                        Ingredients = dto.Ingredients is null
                            ? new Dictionary<string, double>()
                            : new Dictionary<string, double>(dto.Ingredients),
                    };
                }
            }

            _waresByName[name] = new Ware
            {
                Name = name,
                WareId = wareIdMap?.GetValueOrDefault(name),
                Category = itemTypeMap.GetValueOrDefault(name),
                Volume = wareVolumeMap?.GetValueOrDefault(name) ?? 0.0,
                TransportType = wareTransportMap?.GetValueOrDefault(name),
                DefaultFaction = itemFactionMap.GetValueOrDefault(name),
                RecipesByFaction = recipesByFaction,
                ProducibleByFactions = recipesByFaction.Keys.ToArray(),
            };
        }

        _allWares = _waresByName.Values
            .OrderBy(w => CategorySortIndex(w.Category))
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _waresById = new Dictionary<string, Ware>(StringComparer.OrdinalIgnoreCase);
        foreach (var ware in _waresByName.Values)
        {
            if (ware.WareId is not null && !_waresById.ContainsKey(ware.WareId))
            {
                _waresById[ware.WareId] = ware;
            }
        }
    }

    /// <summary>All known wares, ordered by category sort index then name.</summary>
    public IReadOnlyList<Ware> AllWares => _allWares;

    /// <summary>Category → sort index, from <c>ItemTypeSortMap.json</c>.</summary>
    public IReadOnlyDictionary<string, int> CategorySortOrder => _categorySortOrder;

    /// <summary>Case-insensitive lookup by ware name.</summary>
    public Ware? GetByName(string name) =>
        _waresByName.TryGetValue(name, out var ware) ? ware : null;

    /// <summary>Lookup by internal X4 ware id (e.g. "foodrations"); null if unknown or unscanned.</summary>
    public Ware? GetByWareId(string wareId) =>
        _waresById.TryGetValue(wareId, out var ware) ? ware : null;

    /// <summary>Case-insensitive try-lookup by ware name.</summary>
    public bool TryGet(string name, out Ware ware)
    {
        if (_waresByName.TryGetValue(name, out var found))
        {
            ware = found;
            return true;
        }

        ware = null!;
        return false;
    }

    /// <summary>Sort index for a category; uncategorised wares sort last.</summary>
    public int CategorySortIndex(string? category) =>
        category is not null && _categorySortOrder.TryGetValue(category, out var index)
            ? index
            : int.MaxValue;

    private static T LoadMap<T>(string fileName)
    {
        var assembly = typeof(WareRepository).Assembly;
        var resourceName = ResourcePrefix + fileName;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<T>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize '{fileName}'.");
    }

    private sealed class RecipeDto
    {
        public int Amount { get; set; }
        public double WorkforceMultiplier { get; set; }
        public int WorkforceCapacity { get; set; }
        public Dictionary<string, double>? Ingredients { get; set; }
    }
}
