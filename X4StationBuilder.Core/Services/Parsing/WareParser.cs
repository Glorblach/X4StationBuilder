using System.Globalization;
using System.Xml.Linq;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Builds <see cref="WareMaps"/> from the merged <c>libraries/wares.xml</c> records, resolving
/// localized ware names and per-faction production recipes.
/// </summary>
public static class WareParser
{
    private static readonly Dictionary<string, string> MethodToFaction = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = "Common",
        ["argon"] = "Argon",
        ["paranid"] = "Paranid",
        ["teladi"] = "Teladi",
        ["split"] = "Split",
        ["terran"] = "Terran",
        ["boron"] = "Boron",
        ["xenon"] = "Xenon",
    };

    public static WareMaps Parse(
        IEnumerable<XmlLibraryMerger.MergedElement> wareElements,
        LocalizationTable localization)
    {
        var elements = wareElements.Select(e => e.Element).ToList();

        // Pass 1: map internal ware id -> display name (used as the canonical key everywhere).
        var idToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ware in elements)
        {
            var id = ware.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id) || idToName.ContainsKey(id))
            {
                continue;
            }

            var name = localization.Resolve(ware.Attribute("name")?.Value);
            idToName[id] = string.IsNullOrWhiteSpace(name) ? id : name!;
        }

        var maps = new WareMaps();

        foreach (var ware in elements)
        {
            var id = ware.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id) || !idToName.TryGetValue(id, out var wareName))
            {
                continue;
            }

            // Station modules are themselves wares (tagged "module") whose "production" is the
            // module's build recipe (build materials, no workforce). They are not produced
            // commodities, so exclude them from the recipe/ware data used for production planning —
            // but capture their localized name keyed by the macro they place, so the UI can label
            // each production requirement by its module (e.g. "Energy Cell Production").
            if (IsModuleWare(ware))
            {
                var macroRef = ware.Element("component")?.Attribute("ref")?.Value;
                if (!string.IsNullOrEmpty(macroRef) && !maps.ModuleNamesByMacro.ContainsKey(macroRef))
                {
                    maps.ModuleNamesByMacro[macroRef] = wareName;
                }

                continue;
            }

            if (!maps.ItemTypeMap.ContainsKey(wareName))
            {
                maps.ItemTypeMap[wareName] = ware.Attribute("group")?.Value ?? "Uncategorised";
            }

            if (!maps.WareIdMap.ContainsKey(wareName))
            {
                maps.WareIdMap[wareName] = id;
            }

            var transport = ware.Attribute("transport")?.Value;
            if (!string.IsNullOrWhiteSpace(transport) && !maps.WareTransportMap.ContainsKey(wareName))
            {
                maps.WareTransportMap[wareName] = transport!.ToLowerInvariant();
            }

            var volume = ParseDouble(ware.Attribute("volume")?.Value);
            if (volume is > 0 && !maps.WareVolumeMap.ContainsKey(wareName))
            {
                maps.WareVolumeMap[wareName] = volume.Value;
            }

            var productions = ware.Elements("production").ToList();
            if (productions.Count == 0)
            {
                continue;
            }

            if (!maps.RecipeMap.TryGetValue(wareName, out var factionRecipes))
            {
                factionRecipes = new Dictionary<string, RecipeData>(StringComparer.OrdinalIgnoreCase);
                maps.RecipeMap[wareName] = factionRecipes;
            }

            foreach (var production in productions)
            {
                var method = production.Attribute("method")?.Value ?? "default";
                var faction = MethodToFaction.TryGetValue(method, out var f)
                    ? f
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(method);

                if (factionRecipes.ContainsKey(faction))
                {
                    continue;
                }

                factionRecipes[faction] = BuildRecipe(production, idToName);
            }

            if (!maps.ItemFactionMap.ContainsKey(wareName))
            {
                maps.ItemFactionMap[wareName] = factionRecipes.ContainsKey("Common")
                    ? "Common"
                    : factionRecipes.Keys.First();
            }
        }

        return maps;
    }

    private static RecipeData BuildRecipe(XElement production, Dictionary<string, string> idToName)
    {
        // X4 production "amount" is per cycle and "time" is the cycle length in seconds; cycle times
        // vary widely per ware (e.g. 60s for energy cells, 720s for advanced electronics). Normalise
        // everything to a per-hour rate so module counts and cross-ware ratios are correct (this also
        // matches the per-hour convention of the bundled fallback maps).
        var time = ParseInt(production.Attribute("time")?.Value);
        var perHour = time > 0 ? 3600.0 / time : 1.0;

        var recipe = new RecipeData
        {
            Amount = (int)Math.Round(ParseInt(production.Attribute("amount")?.Value) * perHour),
        };

        var workProduct = production
            .Element("effects")?
            .Elements("effect")
            .Where(e => string.Equals(e.Attribute("type")?.Value, "work", StringComparison.OrdinalIgnoreCase))
            .Select(e => ParseDouble(e.Attribute("product")?.Value))
            .FirstOrDefault() ?? 0.0;
        recipe.WorkforceMultiplier = 1.0 + workProduct;

        var primary = production.Element("primary");
        if (primary is not null)
        {
            foreach (var ingredient in primary.Elements("ware"))
            {
                var ingredientId = ingredient.Attribute("ware")?.Value;
                if (string.IsNullOrEmpty(ingredientId))
                {
                    continue;
                }

                var key = idToName.TryGetValue(ingredientId, out var name) ? name : ingredientId;
                recipe.Ingredients[key] = (ParseDouble(ingredient.Attribute("amount")?.Value) ?? 0.0) * perHour;
            }
        }

        return recipe;
    }

    /// <summary>True when the ware is a station module (its "production" is a build recipe).</summary>
    private static bool IsModuleWare(XElement ware)
    {
        var tags = ware.Attribute("tags")?.Value;
        if (string.IsNullOrEmpty(tags))
        {
            return false;
        }

        // Tags are a space/comma separated token list (e.g. "container economy stationbuilding").
        return tags
            .Split(new[] { ' ', ',', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(t => t.Equals("module", StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
