using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class StoragePlannerTests
{
    private static WareRepository Wares()
    {
        var maps = new WareMaps();

        void Add(string name, string transport, double volume)
        {
            maps.ItemTypeMap[name] = "Test";
            maps.WareTransportMap[name] = transport;
            maps.WareVolumeMap[name] = volume;
        }

        Add("Energy cell", "container", 1);
        Add("Hull part", "container", 8);
        Add("Ore", "solid", 10);
        Add("Hydrogen", "liquid", 6);

        return WareRepository.FromWareMaps(maps);
    }

    private static StationModule Storage(string id, CargoType cargo, int capacity, ModuleSize size = ModuleSize.L) => new()
    {
        Id = id,
        Name = id,
        Macro = id + "_macro",
        Kind = ModuleKind.Storage,
        Size = size,
        CargoType = cargo,
        CargoCapacity = capacity,
    };

    private static StationModule FactionStorage(string id, string faction, int capacity) => new()
    {
        Id = id,
        Name = id,
        Macro = id + "_macro",
        Kind = ModuleKind.Storage,
        Size = ModuleSize.L,
        CargoType = CargoType.Container,
        CargoCapacity = capacity,
        Factions = [faction],
    };

    private static ModuleCatalog Catalog(params StationModule[] storage) => new(storage);

    private static FactoryGroup Group(WareRepository wares, string wareName, double itemsPerHour) => new()
    {
        Ware = wares.GetByName(wareName)!,
        Faction = "Common",
        Recipe = new Recipe { Faction = "Common", Amount = 1 },
        ItemCount = itemsPerHour,
    };

    private static ProductionResult Result(
        IReadOnlyList<FactoryGroup> groups,
        IReadOnlyDictionary<string, double>? raw = null) => new()
    {
        RequiredFactoryGroups = groups,
        TotalRawResources = raw ?? new Dictionary<string, double>(),
        UnproducibleDesiredWares = [],
        CyclicWares = [],
    };

    [Fact]
    public void Plan_GroupsByCargoClass_AndSizesOneHour()
    {
        var wares = Wares();
        var catalog = Catalog(
            Storage("storage_container", CargoType.Container, 50000),
            Storage("storage_solid", CargoType.Solid, 40000),
            Storage("storage_liquid", CargoType.Liquid, 30000));

        var result = Result(
            [Group(wares, "Energy cell", 1000)], // 1000 m³ container
            new Dictionary<string, double> { ["Ore"] = 500, ["Hydrogen"] = 200 }); // 5000 solid, 1200 liquid

        var plan = new StoragePlanner(wares, catalog).Plan(result, hours: 1.0);

        Assert.Equal(3, plan.Count);
        Assert.Equal(1, plan.Single(i => i.Module.CargoType == CargoType.Container).Count);
        Assert.Equal(1, plan.Single(i => i.Module.CargoType == CargoType.Solid).Count);
        Assert.Equal(1, plan.Single(i => i.Module.CargoType == CargoType.Liquid).Count);
    }

    [Fact]
    public void Plan_PrefersStorageOfTheGivenFaction()
    {
        var wares = Wares();
        // Same capacity, different factions: faction preference must decide.
        var catalog = Catalog(
            FactionStorage("stor_arg_container", "argon", 50000),
            FactionStorage("stor_ter_container", "terran", 50000));

        var result = Result([Group(wares, "Energy cell", 1000)]);

        var terran = new StoragePlanner(wares, catalog).Plan(result, preferredFaction: "Terran");
        Assert.Equal("stor_ter_container", Assert.Single(terran).Module.Id);

        // No preference → falls back to (largest, then first) — here the Argon one by capacity order.
        var any = new StoragePlanner(wares, catalog).Plan(result);
        Assert.Single(any);

        // A faction with no storage falls back to any rather than returning nothing.
        var fallback = new StoragePlanner(wares, catalog).Plan(result, preferredFaction: "Boron");
        Assert.Single(fallback);
    }

    [Fact]
    public void Plan_RoundsModuleCountUp()
    {
        var wares = Wares();
        var catalog = Catalog(Storage("storage_container", CargoType.Container, 50000));

        // 60,000 units × 1 m³ = 60,000 m³ over a 50,000 m³ module → ceil(1.2) = 2.
        var result = Result([Group(wares, "Energy cell", 60000)]);

        var plan = new StoragePlanner(wares, catalog).Plan(result);

        Assert.Equal(2, Assert.Single(plan).Count);
    }

    [Fact]
    public void Plan_PicksLargestCapacityModulePerClass()
    {
        var wares = Wares();
        var catalog = Catalog(
            Storage("storage_container_s", CargoType.Container, 10000, ModuleSize.S),
            Storage("storage_container_l", CargoType.Container, 60000, ModuleSize.L));

        var result = Result([Group(wares, "Energy cell", 60000)]); // 60,000 m³

        var item = Assert.Single(new StoragePlanner(wares, catalog).Plan(result));
        Assert.Equal("storage_container_l", item.Module.Id);
        Assert.Equal(1, item.Count); // fits in the single 60,000 m³ module
    }

    [Fact]
    public void Plan_FallsBackToSizeCapacity_WhenMacroCapacityMissing()
    {
        var wares = Wares();
        // CargoCapacity = 0 → planner uses the per-size fallback (L = 54,000 m³).
        var catalog = Catalog(Storage("storage_container", CargoType.Container, 0, ModuleSize.L));

        var result = Result([Group(wares, "Energy cell", 54000)]); // 54,000 m³ → exactly one L module

        var item = Assert.Single(new StoragePlanner(wares, catalog).Plan(result));
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public void Plan_UsesFallbackVolume_WhenWareVolumeUnknown()
    {
        // Ware "Mystery" has no volume/transport → classified container, fallback volume 12 m³.
        var maps = new WareMaps();
        maps.ItemTypeMap["Mystery"] = "Test";
        var wares = WareRepository.FromWareMaps(maps);
        var catalog = Catalog(Storage("storage_container", CargoType.Container, 12000));

        var result = Result([Group(wares, "Mystery", 1000)]); // 1000 × 12 = 12,000 m³ → 1 module

        var item = Assert.Single(new StoragePlanner(wares, catalog).Plan(result));
        Assert.Equal(CargoType.Container, item.Module.CargoType);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public void Plan_ReturnsEmpty_WhenNothingFlows()
    {
        var wares = Wares();
        var catalog = Catalog(Storage("storage_container", CargoType.Container, 50000));

        var plan = new StoragePlanner(wares, catalog).Plan(Result([]));

        Assert.Empty(plan);
    }
}
