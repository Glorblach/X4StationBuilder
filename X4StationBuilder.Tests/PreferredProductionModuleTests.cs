using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class PreferredProductionModuleTests
{
    [Fact]
    public void Layout_PlacesUserChosenProductionModule()
    {
        var wares = new WareRepository();
        var catalog = ModuleCatalog.FromBundledSeed();
        var calculator = new ProductionCalculator(wares, catalog);

        // Choose the Terran energy-cell module specifically.
        var result = calculator.Calculate(
            [new DesiredWare("Energy cell", 1000, "Common", PreferredModuleId: "prod_ter_energycells")]);

        var group = Assert.Single(result.RequiredFactoryGroups, g =>
            g.WareName.Equals("Energy cell", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("prod_ter_energycells", group.PreferredModuleId);

        var layout = StationLayoutBuilder.Build(result, catalog);
        Assert.Contains(layout.Modules, m => m.Module.Id == "prod_ter_energycells");
        Assert.DoesNotContain(layout.Modules, m => m.Module.Id == "prod_arg_energycells");
    }

    [Fact]
    public void Layout_FallsBackToDefaultModule_WhenNoPreferenceGiven()
    {
        var wares = new WareRepository();
        var catalog = ModuleCatalog.FromBundledSeed();
        var calculator = new ProductionCalculator(wares, catalog);

        var result = calculator.Calculate([new DesiredWare("Energy cell", 1000)]);
        var layout = StationLayoutBuilder.Build(result, catalog);

        Assert.Contains(layout.Modules,
            m => m.Module.Id.Equals("prod_arg_energycells", StringComparison.OrdinalIgnoreCase));
    }
}
