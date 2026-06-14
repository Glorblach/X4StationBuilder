using System.Xml.Linq;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Builds <see cref="StationModule"/> records by combining merged <c>libraries/modules.xml</c>
/// definitions with the macro names resolved from <c>libraries/modulegroups.xml</c>.
/// </summary>
public static class ModuleParser
{
    public static List<StationModule> Parse(
        IEnumerable<XmlLibraryMerger.MergedElement> moduleElements,
        IEnumerable<XmlLibraryMerger.MergedElement> groupElements,
        IReadOnlyDictionary<string, int>? macroWorkforce = null,
        IReadOnlyDictionary<string, string>? macroNames = null,
        IReadOnlyDictionary<string, ModuleGeometry>? macroGeometry = null,
        IReadOnlyDictionary<string, int>? macroCargo = null)
    {
        var groupToMacros = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupElements)
        {
            var name = group.Element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name) || groupToMacros.ContainsKey(name))
            {
                continue;
            }

            // A module group can list several <select> variants (e.g. an Argon harbor pier offers the
            // T, E and other dock layouts). Each is a distinct, placeable macro, so capture them all.
            var macros = group.Element.Elements("select")
                .Select(s => s.Attribute("macro")?.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (macros.Count > 0)
            {
                groupToMacros[name] = macros;
            }
        }

        var modules = new List<StationModule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (element, sourceExtension) in moduleElements)
        {
            var id = element.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            var group = element.Attribute("group")?.Value;
            var category = element.Element("category");
            var kind = ClassifyKind(id);
            var producedWare = category?.Attribute("ware")?.Value;
            var factions = ParseBracketList(category?.Attribute("faction")?.Value);
            var races = ParseBracketList(category?.Attribute("race")?.Value);

            var macros = group is not null && groupToMacros.TryGetValue(group, out var list) ? list : null;

            if (macros is null || macros.Count == 0)
            {
                modules.Add(BuildModule(
                    id, null, group, kind, producedWare, factions, races,
                    sourceExtension, macroWorkforce, macroNames, macroGeometry, macroCargo));
                continue;
            }

            // Emit one module per variant macro. The first keeps the base id for back-compat; extra
            // variants get a derived, unique id so they coexist in the catalog (and are deduped by
            // macro where it matters, e.g. the dock picker).
            for (var i = 0; i < macros.Count; i++)
            {
                var variantId = i == 0 ? id : $"{id}_v{i}";
                modules.Add(BuildModule(
                    variantId, macros[i], group, kind, producedWare, factions, races,
                    sourceExtension, macroWorkforce, macroNames, macroGeometry, macroCargo));
            }
        }

        return modules;
    }

    private static StationModule BuildModule(
        string id,
        string? macro,
        string? group,
        ModuleKind kind,
        string? producedWare,
        IReadOnlyList<string> factions,
        IReadOnlyList<string> races,
        string? sourceExtension,
        IReadOnlyDictionary<string, int>? macroWorkforce,
        IReadOnlyDictionary<string, string>? macroNames,
        IReadOnlyDictionary<string, ModuleGeometry>? macroGeometry,
        IReadOnlyDictionary<string, int>? macroCargo)
    {
        var workforce = macro is not null
            && macroWorkforce is not null
            && macroWorkforce.TryGetValue(macro, out var cap)
                ? cap
                : 0;

        var cargoCapacity = macro is not null
            && macroCargo is not null
            && macroCargo.TryGetValue(macro, out var cc)
                ? cc
                : 0;

        var name = macro is not null
            && macroNames is not null
            && macroNames.TryGetValue(macro, out var n)
                ? n
                : null;

        var geometry = macro is not null
            && macroGeometry is not null
            && macroGeometry.TryGetValue(macro, out var g)
                ? g
                : null;

        return new StationModule
        {
            Id = id,
            Name = name,
            Group = group,
            Macro = macro,
            Kind = kind,
            Size = ParseSize(macro ?? id),
            CargoType = kind == ModuleKind.Storage ? ParseCargoType(macro ?? id) : CargoType.None,
            ProducedWare = producedWare,
            Factions = factions,
            Races = races,
            WorkforceCapacity = workforce,
            CargoCapacity = cargoCapacity,
            SourceExtension = sourceExtension,
            Geometry = geometry,
        };
    }

    private static ModuleSize ParseSize(string macroOrId)
    {
        foreach (var token in macroOrId.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "s": return ModuleSize.S;
                case "m": return ModuleSize.M;
                case "l": return ModuleSize.L;
                case "xl": return ModuleSize.XL;
            }
        }

        return ModuleSize.Unknown;
    }

    private static CargoType ParseCargoType(string macroOrId)
    {
        var lower = macroOrId.ToLowerInvariant();
        if (lower.Contains("container")) return CargoType.Container;
        if (lower.Contains("solid")) return CargoType.Solid;
        if (lower.Contains("liquid")) return CargoType.Liquid;
        return CargoType.None;
    }

    private static ModuleKind ClassifyKind(string id)
    {
        var prefix = id.Split('_', 2)[0].ToLowerInvariant();
        return prefix switch
        {
            "prod" => ModuleKind.Production,
            "stor" => ModuleKind.Storage,
            "dockarea" => ModuleKind.Dock,
            "pier" => ModuleKind.Pier,
            "hab" => ModuleKind.Habitat,
            "def" => ModuleKind.Defence,
            "build" => ModuleKind.Build,
            "conn" => ModuleKind.Connection,
            "equip" => ModuleKind.Equipment,
            "welfare" => ModuleKind.Welfare,
            "processing" => ModuleKind.Processing,
            "recycling" => ModuleKind.Recycling,
            _ => ModuleKind.Other,
        };
    }

    private static IReadOnlyList<string> ParseBracketList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Trim()
            .Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
