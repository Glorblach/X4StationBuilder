using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class ModuleCatalogTests
{
    private static readonly WareRepository Wares = new();
    private static readonly ModuleCatalog Catalog = ModuleCatalog.FromBundledSeed();

    [Fact]
    public void GetProductionModule_EnergyCellsArgon_ReturnsEnergyCellsMacro()
    {
        var ware = Wares.GetByName("Energy cell");
        Assert.NotNull(ware);

        var module = Catalog.GetProductionModule(ware!, "Argon");

        Assert.NotNull(module);
        Assert.Equal(ModuleKind.Production, module!.Kind);
        Assert.NotNull(module.Macro);
        Assert.StartsWith("prod_", module.Macro!);
        Assert.Contains("energycells", module.Macro!);
    }

    [Fact]
    public void GetProductionModuleName_PrefersModuleNameThenFallsBackToWare()
    {
        var named = new ModuleCatalog(new[]
        {
            new StationModule
            {
                Id = "prod_gen_energycells",
                Macro = "prod_gen_energycells_macro",
                Name = "Energy Cell Production",
                Kind = ModuleKind.Production,
                ProducedWare = "energycells",
                Factions = ["argon", "teladi"],
            },
        });
        // "Common" recipe faction has no matching module owner faction, but name still resolves.
        var ware = new Ware { Name = "Energy Cells", WareId = "energycells" };
        Assert.Equal("Energy Cell Production", named.GetProductionModuleName(ware, "Common"));

        // No name on the module → derived label from the ware.
        var unnamed = new ModuleCatalog(new[]
        {
            new StationModule
            {
                Id = "prod_gen_energycells",
                Kind = ModuleKind.Production,
                ProducedWare = "energycells",
            },
        });
        Assert.Equal("Energy Cells Production", unnamed.GetProductionModuleName(ware));
    }

    [Fact]
    public void GetProductionModule_MatchesByInternalWareId()
    {
        var catalog = new ModuleCatalog(new[]
        {
            new StationModule
            {
                Id = "prod_arg_energycells",
                Macro = "prod_arg_energycells_01_macro",
                Kind = ModuleKind.Production,
                ProducedWare = "energycells",
            },
        });
        var ware = new Ware { Name = "Some Display Name", WareId = "energycells" };

        var module = catalog.GetProductionModule(ware);

        Assert.NotNull(module);
        Assert.Contains("energycells", module!.Macro!);
    }

    [Fact]
    public void GetHabitatModules_ReturnsAtLeastOneWithWorkerCapacity()
    {
        var habitats = Catalog.GetHabitatModules();

        Assert.NotEmpty(habitats);
        Assert.All(habitats, h => Assert.Equal(ModuleKind.Habitat, h.Kind));
        Assert.Contains(habitats, h => h.WorkforceCapacity > 0);
    }

    [Fact]
    public void GetDockModules_ReturnsAtLeastOne()
    {
        var docks = Catalog.GetDockModules();

        Assert.NotEmpty(docks);
        Assert.All(docks, d => Assert.Equal(ModuleKind.Dock, d.Kind));
    }

    [Theory]
    [InlineData(CargoType.Container)]
    [InlineData(CargoType.Solid)]
    [InlineData(CargoType.Liquid)]
    public void GetStorageModules_ReturnsModuleForEachCargoType(CargoType cargoType)
    {
        var storage = Catalog.GetStorageModules(cargoType);

        Assert.NotEmpty(storage);
        Assert.All(storage, s => Assert.Equal(cargoType, s.CargoType));
    }

    [Fact]
    public void CreatePreferringScanned_FallsBackToBundledSeedWhenNoScan()
    {
        var dir = Path.Combine(Path.GetTempPath(), "x4sb-catalog-" + Guid.NewGuid().ToString("N"));
        var store = new ScannedDataStore(dir);

        var catalog = ModuleCatalog.CreatePreferringScanned(store);

        Assert.NotEmpty(catalog.Modules);
        Assert.NotEmpty(catalog.GetDockModules());
    }
}
