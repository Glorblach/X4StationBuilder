using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Sizes on-station storage to buffer roughly N hours of throughput. For each cargo class
/// (container/solid/liquid) it sums the hourly volume of every ware that flows through the station —
/// raw resource inputs plus every produced ware (including intermediates) — and picks enough of the
/// largest available storage module of that class to hold it.
/// </summary>
/// <remarks>
/// Accurate sizing needs scanned data: per-ware <see cref="Ware.Volume"/>/<see cref="Ware.TransportType"/>
/// (from <c>wares.xml</c>) and per-module <see cref="StationModule.CargoCapacity"/> (from the storage
/// macro). When those are absent (bundled/no-scan data) the planner falls back to built-in default
/// volumes and per-<see cref="ModuleSize"/> capacities, so it still produces a reasonable estimate.
/// </remarks>
public sealed class StoragePlanner
{
    /// <summary>Default buffer window in hours.</summary>
    public const double DefaultHours = 1.0;

    private const int DefaultCapacity = 18000;

    /// <summary>Fallback storage capacity (m³) per module size when the macro capacity is unknown.</summary>
    private static readonly Dictionary<ModuleSize, int> FallbackCapacity = new()
    {
        [ModuleSize.S] = 6000,
        [ModuleSize.M] = 18000,
        [ModuleSize.L] = 54000,
        [ModuleSize.XL] = 54000,
        [ModuleSize.Unknown] = DefaultCapacity,
    };

    /// <summary>Fallback per-unit volume (m³) per cargo class when a ware's volume is unknown.</summary>
    private static readonly Dictionary<CargoType, double> FallbackVolume = new()
    {
        [CargoType.Container] = 12,
        [CargoType.Solid] = 12,
        [CargoType.Liquid] = 8,
    };

    private readonly WareRepository _wares;
    private readonly ModuleCatalog _catalog;

    public StoragePlanner(WareRepository wares, ModuleCatalog catalog)
    {
        _wares = wares ?? throw new ArgumentNullException(nameof(wares));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Returns the storage modules (with counts) needed to buffer <paramref name="hours"/> of the
    /// plan's throughput, or an empty list when there is nothing to store. When
    /// <paramref name="preferredFaction"/> is given, storage of that faction is chosen when available
    /// (so the station's storage matches the chosen species), falling back to any faction otherwise.
    /// <paramref name="minContainerModules"/> sets a floor on container storage (used when build
    /// modules are present so a wharf/shipyard always gets at least some container storage).
    /// </summary>
    public IReadOnlyList<LayoutItem> Plan(
        ProductionResult result,
        double hours = DefaultHours,
        string? preferredFaction = null,
        int minContainerModules = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (hours <= 0)
        {
            return [];
        }

        var volumeByType = new Dictionary<CargoType, double>();

        void Accumulate(string wareName, double itemsPerHour)
        {
            if (itemsPerHour <= 0 || string.IsNullOrWhiteSpace(wareName))
            {
                return;
            }

            var ware = _wares.GetByName(wareName);
            var type = ClassifyCargo(ware);
            if (type == CargoType.None)
            {
                return;
            }

            var unitVolume = ware is { Volume: > 0 } ? ware.Volume : FallbackVolume[type];
            volumeByType[type] = volumeByType.GetValueOrDefault(type) + itemsPerHour * unitVolume * hours;
        }

        // Every produced ware (including intermediates consumed downstream).
        foreach (var group in result.RequiredFactoryGroups)
        {
            Accumulate(group.WareName, group.ItemCount);
        }

        // Raw resource inputs pulled from outside the station.
        foreach (var (ware, rate) in result.TotalRawResources)
        {
            Accumulate(ware, rate);
        }

        var items = new List<LayoutItem>();
        foreach (var type in new[] { CargoType.Container, CargoType.Solid, CargoType.Liquid })
        {
            if (!volumeByType.TryGetValue(type, out var totalVolume) || totalVolume <= 0)
            {
                continue;
            }

            var module = LargestStorageModule(type, preferredFaction);
            if (module is null)
            {
                continue;
            }

            var capacity = EffectiveCapacity(module);
            var count = (int)Math.Ceiling(totalVolume / capacity);
            if (count > 0)
            {
                items.Add(new LayoutItem(module, count));
            }
        }

        // Floor: guarantee a minimum amount of container storage (e.g. for a pure wharf/shipyard that
        // produces nothing to size storage from). Tops up the existing container item or adds one.
        if (minContainerModules > 0)
        {
            var existing = items.FirstOrDefault(i => i.Module.CargoType == CargoType.Container);
            if (existing is not null)
            {
                if (existing.Count < minContainerModules)
                {
                    items[items.IndexOf(existing)] = new LayoutItem(existing.Module, minContainerModules);
                }
            }
            else
            {
                var module = LargestStorageModule(CargoType.Container, preferredFaction);
                if (module is not null)
                {
                    items.Insert(0, new LayoutItem(module, minContainerModules));
                }
            }
        }

        return items;
    }

    /// <summary>Maps a ware to its storage cargo class via its transport type, defaulting to container.</summary>
    private static CargoType ClassifyCargo(Ware? ware)
    {
        var transport = ware?.TransportType?.ToLowerInvariant();
        if (transport is not null)
        {
            if (transport.Contains("solid")) return CargoType.Solid;
            if (transport.Contains("liquid")) return CargoType.Liquid;
            if (transport.Contains("container")) return CargoType.Container;
        }

        // Unknown transport (e.g. bundled data): treat as container, the most common goods class.
        return CargoType.Container;
    }

    /// <summary>
    /// The highest-capacity storage module of the given cargo class, preferring
    /// <paramref name="faction"/> when supplied and available, else any faction. Null if none exist.
    /// </summary>
    private StationModule? LargestStorageModule(CargoType type, string? faction = null)
    {
        var ofType = _catalog.GetStorageModules(type);

        if (!string.IsNullOrWhiteSpace(faction))
        {
            var factionMatch = ofType
                .Where(m => m.Factions.Any(f => string.Equals(f, faction, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(EffectiveCapacity)
                .FirstOrDefault();
            if (factionMatch is not null)
            {
                return factionMatch;
            }
        }

        return ofType.OrderByDescending(EffectiveCapacity).FirstOrDefault();
    }

    private static int EffectiveCapacity(StationModule module) =>
        module.CargoCapacity > 0
            ? module.CargoCapacity
            : FallbackCapacity.GetValueOrDefault(module.Size, DefaultCapacity);
}
