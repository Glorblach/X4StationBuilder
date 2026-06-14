using Newtonsoft.Json;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Data;

/// <summary>
/// Resolves a <see cref="Ware"/> to its display category group (e.g. "Refined Goods",
/// "High Tech Goods"), mirroring the grouping used by the x4-game.com station calculator.
/// </summary>
/// <remarks>
/// Backed by the embedded <c>Data/Maps/WareGroups.json</c>, which is a static bake of the
/// x4-game.com group definitions and ware→group mapping. The group ids (<c>energy</c>,
/// <c>refined</c>, <c>hightech</c>, …) are the same ids X4 stores in <c>wares.xml</c>'s
/// <c>group</c> attribute, which the scanner captures into <see cref="Ware.Category"/> — so scanned
/// wares resolve directly by category id, with ware-id/name maps as a fallback for bundled data.
/// </remarks>
public sealed class WareGroupResolver
{
    private const string ResourceName = "X4StationBuilder.Core.Data.Maps.WareGroups.json";

    /// <summary>Group assigned to wares that don't resolve to any known group; sorts last.</summary>
    public const string OtherGroupName = "Other";

    private const int OtherGroupOrder = 999;

    private readonly Dictionary<string, GroupInfo> _groupsById;
    private readonly Dictionary<string, string> _groupNameToOrderKey;
    private readonly Dictionary<string, int> _orderByName;
    private readonly Dictionary<string, string> _waresById;
    private readonly Dictionary<string, string> _normalizedNameToGroupId;

    /// <summary>Loads the resolver from the bundled embedded <c>WareGroups.json</c>.</summary>
    public WareGroupResolver()
        : this(LoadData())
    {
    }

    private WareGroupResolver(WareGroupsDto data)
    {
        _groupsById = new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase);
        _orderByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _groupNameToOrderKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in data.Groups ?? [])
        {
            if (string.IsNullOrEmpty(group.Id) || string.IsNullOrEmpty(group.Name))
            {
                continue;
            }

            _groupsById[group.Id] = new GroupInfo(group.Name!, group.Order);
            _orderByName[group.Name!] = group.Order;
        }

        _orderByName[OtherGroupName] = OtherGroupOrder;

        _waresById = data.WaresById is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(data.WaresById, StringComparer.OrdinalIgnoreCase);

        _normalizedNameToGroupId = new Dictionary<string, string>(StringComparer.Ordinal);
        AddNormalizedKeys(data.WaresByName);
        // Ware ids are spaceless internal names; index them too for normalized fallback matching.
        AddNormalizedKeys(data.WaresById);
    }

    /// <summary>Resolves the display group name for a ware (or "Other" when unknown).</summary>
    public string GetGroupName(Ware ware) => Resolve(ware).Name;

    /// <summary>Sort order for a group display name (lower sorts first; unknown sorts last).</summary>
    public int GetOrder(string groupName) =>
        _orderByName.TryGetValue(groupName, out var order) ? order : OtherGroupOrder;

    /// <summary>Resolves a ware to its group display name and sort order.</summary>
    public (string Name, int Order) Resolve(Ware ware)
    {
        // 1) Scanned data: Category holds the X4 group id (matches our group ids directly).
        if (!string.IsNullOrWhiteSpace(ware.Category)
            && _groupsById.TryGetValue(ware.Category!, out var byCategory))
        {
            return (byCategory.Name, byCategory.Order);
        }

        // 2) Internal ware id (e.g. "refinedmetals").
        if (!string.IsNullOrWhiteSpace(ware.WareId)
            && _waresById.TryGetValue(ware.WareId!, out var idGroup)
            && _groupsById.TryGetValue(idGroup, out var byId))
        {
            return (byId.Name, byId.Order);
        }

        // 3) Display-name fallback (bundled data has no ware id), tolerant of case/spacing/plurals.
        var normalized = Normalize(ware.Name);
        if (normalized.Length > 0
            && (_normalizedNameToGroupId.TryGetValue(normalized, out var nameGroup)
                || _normalizedNameToGroupId.TryGetValue(Singularize(normalized), out nameGroup))
            && _groupsById.TryGetValue(nameGroup, out var byName))
        {
            return (byName.Name, byName.Order);
        }

        return (OtherGroupName, OtherGroupOrder);
    }

    private void AddNormalizedKeys(Dictionary<string, string>? map)
    {
        if (map is null)
        {
            return;
        }

        foreach (var (key, groupId) in map)
        {
            var normalized = Normalize(key);
            if (normalized.Length == 0)
            {
                continue;
            }

            _normalizedNameToGroupId.TryAdd(normalized, groupId);
            _normalizedNameToGroupId.TryAdd(Singularize(normalized), groupId);
        }
    }

    private static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }

    private static string Singularize(string value)
    {
        if (value.Length > 3 && value.EndsWith("ies"))
        {
            return value[..^3] + "y";
        }

        return value.Length > 1 && value.EndsWith('s') ? value[..^1] : value;
    }

    private static WareGroupsDto LoadData()
    {
        var assembly = typeof(WareGroupResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        return JsonConvert.DeserializeObject<WareGroupsDto>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Failed to deserialize WareGroups.json.");
    }

    private readonly record struct GroupInfo(string Name, int Order);

    private sealed class WareGroupsDto
    {
        public List<GroupDto>? Groups { get; set; }
        public Dictionary<string, string>? WaresById { get; set; }
        public Dictionary<string, string>? WaresByName { get; set; }
    }

    private sealed class GroupDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Order { get; set; }
    }
}
