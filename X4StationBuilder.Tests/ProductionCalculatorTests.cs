using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class ProductionCalculatorTests
{
    private static readonly WareRepository Repository = new();
    private static readonly ProductionCalculator Calculator = new(Repository);

    private static FactoryGroup? Group(ProductionResult result, string wareName) =>
        result.RequiredFactoryGroups.FirstOrDefault(
            g => string.Equals(g.WareName, wareName, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotARealFaction")]
    public void Workforce_WithMissingOrNullFaction_DoesNotThrow(string? faction)
    {
        var options = new ProductionOptions
        {
            WorkforceEnabled = true,
            WorkforceFaction = faction!,
        };

        // A null/blank/unknown workforce faction must resolve to a valid fallback, not crash.
        var result = Calculator.Calculate([new DesiredWare("Hull parts", 1000)], options);

        Assert.NotNull(result);
        Assert.True(result.WorkforceEnabled);
    }

    [Fact]
    public void SimpleChain_ResolvesIntermediatesAndRawResources()
    {
        var result = Calculator.Calculate([new DesiredWare("Antimatter cell", 3300)]);

        var antimatter = Group(result, "Antimatter cell");
        Assert.NotNull(antimatter);
        Assert.Equal(3300, antimatter!.ItemCount, 3);
        Assert.Equal(1, antimatter.StationCount, 3);

        // Energy cell consumption = 3000/cycle * 1 station = 3000/hr.
        var energy = Group(result, "Energy cell");
        Assert.NotNull(energy);
        Assert.Equal(3000, energy!.ItemCount, 3);

        // Hydrogen has no recipe → it is a raw resource, not a module.
        Assert.Null(Group(result, "Hydrogen"));
        Assert.Equal(9600, result.TotalRawResources["Hydrogen"], 3);

        Assert.Empty(result.UnproducibleDesiredWares);
        Assert.Empty(result.CyclicWares);
    }

    [Fact]
    public void StationCount_IsFractionalAndCeils()
    {
        var result = Calculator.Calculate([new DesiredWare("Antimatter cell", 6600)]);

        var antimatter = Group(result, "Antimatter cell")!;
        Assert.Equal(2, antimatter.StationCount, 3);
        Assert.Equal(2, antimatter.StationCountCeil);

        // Energy cell: 3000 * 2 = 6000/hr; module output 12000 → 0.5 stations, ceil 1.
        var energy = Group(result, "Energy cell")!;
        Assert.Equal(6000, energy.ItemCount, 3);
        Assert.Equal(0.5, energy.StationCount, 3);
        Assert.Equal(1, energy.StationCountCeil);
    }

    [Fact]
    public void DuplicateWares_AreMergedAcrossBranches()
    {
        var result = Calculator.Calculate(
        [
            new DesiredWare("Antimatter cell", 3300), // → 3000 energy cells/hr
            new DesiredWare("Graphene", 1650),        // → 1200 energy cells/hr
        ]);

        var energyGroups = result.RequiredFactoryGroups
            .Where(g => string.Equals(g.WareName, "Energy cell", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(energyGroups);
        Assert.Equal(4200, energyGroups[0].ItemCount, 3);
    }

    [Fact]
    public void RawResourceTotals_ScaleWithDesiredRate()
    {
        var single = Calculator.Calculate([new DesiredWare("Antimatter cell", 3300)]);
        var doubled = Calculator.Calculate([new DesiredWare("Antimatter cell", 6600)]);

        Assert.Equal(9600, single.TotalRawResources["Hydrogen"], 3);
        Assert.Equal(19200, doubled.TotalRawResources["Hydrogen"], 3);
    }

    [Fact]
    public void DefaultFaction_IsUsedWhenNoOverrideGiven()
    {
        var result = Calculator.Calculate([new DesiredWare("Hull part", 1320)]);

        var hull = Group(result, "Hull part")!;
        Assert.Equal("Teladi", hull.Faction);

        // The Teladi recipe consumes Teladianium, not Refined metal.
        Assert.NotNull(Group(result, "Teladianium"));
        Assert.Null(Group(result, "Refined metal"));
    }

    [Fact]
    public void FactionOverride_SelectsDifferentRecipe()
    {
        var overrides = new Dictionary<string, string> { ["Hull part"] = "Common" };
        var result = Calculator.Calculate([new DesiredWare("Hull part", 880)], overrides);

        var hull = Group(result, "Hull part")!;
        Assert.Equal("Common", hull.Faction);

        // The Common recipe consumes Refined metal, not Teladianium.
        Assert.NotNull(Group(result, "Refined metal"));
        Assert.Null(Group(result, "Teladianium"));
    }

    [Fact]
    public void PerDesiredWareFaction_TakesPrecedence()
    {
        var result = Calculator.Calculate([new DesiredWare("Hull part", 880, Faction: "Common")]);

        Assert.Equal("Common", Group(result, "Hull part")!.Faction);
    }

    [Fact]
    public void UnproducibleDesiredWare_IsReported()
    {
        var result = Calculator.Calculate([new DesiredWare("Hydrogen", 1000)]);

        Assert.Contains("Hydrogen", result.UnproducibleDesiredWares);
        Assert.Empty(result.RequiredFactoryGroups);
    }

    [Fact]
    public void NonPositiveRate_IsIgnored()
    {
        var result = Calculator.Calculate([new DesiredWare("Antimatter cell", 0)]);

        Assert.Empty(result.RequiredFactoryGroups);
        Assert.Empty(result.UnproducibleDesiredWares);
    }

    private static readonly ProductionCalculator CalculatorWithModules =
        new(Repository, ModuleCatalog.FromBundledSeed());

    [Fact]
    public void Workforce_MultiplierReducesStationCount()
    {
        // Energy cell module output is 12000/cycle base, boosted to 12000 * 1.25 when staffed.
        var off = Calculator.Calculate([new DesiredWare("Energy cell", 12000)]);
        var on = Calculator.Calculate(
            [new DesiredWare("Energy cell", 12000)],
            new ProductionOptions { WorkforceEnabled = true });

        var energyOff = Group(off, "Energy cell")!;
        var energyOn = Group(on, "Energy cell")!;

        Assert.Equal(1, energyOff.StationCount, 3);
        Assert.Equal(12000, energyOff.EffectiveAmount, 3);
        Assert.Equal(15000, energyOn.EffectiveAmount, 3); // 12000 * 1.25
        // Same output rate needs fewer modules once staffed (the workforce multiplier boosts output).
        Assert.True(
            energyOn.ItemCount / energyOn.EffectiveAmount < energyOff.ItemCount / energyOff.EffectiveAmount);
        Assert.True(energyOn.WorkforceStaffed);
        Assert.False(energyOff.WorkforceStaffed);
    }

    [Fact]
    public void Workforce_AddsWorkersHabitatsAndFoodMedicalChains()
    {
        var result = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = true, WorkforceFaction = "Argon" });

        Assert.True(result.WorkforceEnabled);
        Assert.True(result.TotalWorkers > 0);

        // Food + medical production chains are added for the workers.
        Assert.NotNull(Group(result, "Food ration"));
        Assert.NotNull(Group(result, "Medical supply"));

        // Habitats are added and house at least all the workers.
        Assert.NotEmpty(result.Habitats);
        var housed = result.Habitats.Sum(h => h.HousedWorkers);
        Assert.True(housed >= result.TotalWorkers);

        // Worker total reconciles with the staffed groups' worker counts.
        var summed = result.RequiredFactoryGroups.Sum(g => g.Workers);
        Assert.Equal(summed, result.TotalWorkers);
    }

    [Fact]
    public void Workforce_Disabled_HasNoWorkersOrHabitats()
    {
        var result = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = false });

        Assert.False(result.WorkforceEnabled);
        Assert.Equal(0, result.TotalWorkers);
        Assert.Empty(result.Habitats);
        Assert.Null(Group(result, "Food ration"));
    }

    [Fact]
    public void Workforce_FactionSelectsDifferentFoodChain()
    {
        var argon = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = true, WorkforceFaction = "Argon" });
        var teladi = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = true, WorkforceFaction = "Teladi" });

        // Argon workforce eats food rations; Teladi eats nostrop oil.
        Assert.NotNull(Group(argon, "Food ration"));
        Assert.Null(Group(argon, "Nostrop oil"));

        Assert.NotNull(Group(teladi, "Nostrop oil"));
        Assert.Null(Group(teladi, "Food ration"));
    }

    [Fact]
    public void Workforce_HabitatsReconcileWithWorkerCount()
    {
        var result = CalculatorWithModules.Calculate(
            [new DesiredWare("Hull part", 4400)],
            new ProductionOptions { WorkforceEnabled = true });

        Assert.True(result.TotalWorkers > 0);
        Assert.NotEmpty(result.Habitats);

        var housed = result.Habitats.Sum(h => h.HousedWorkers);
        Assert.True(housed >= result.TotalWorkers);

        // Minimal habitat count: removing one would leave workers unhoused.
        var habitat = result.Habitats[0];
        Assert.Equal(
            (int)Math.Ceiling((double)result.TotalWorkers / habitat.CapacityPerModule),
            habitat.Count);
    }

    [Fact]
    public void Workforce_ImportSupplies_OmitsFoodMedicalModulesAndListsThemAsRaw()
    {
        var produce = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = true, ProduceWorkforceSupplies = true });
        var import = CalculatorWithModules.Calculate(
            [new DesiredWare("Antimatter cell", 3300)],
            new ProductionOptions { WorkforceEnabled = true, ProduceWorkforceSupplies = false });

        // Producing locally adds the food/medical modules; importing does not.
        Assert.NotNull(Group(produce, "Food ration"));
        Assert.Null(Group(import, "Food ration"));
        Assert.Null(Group(import, "Medical supply"));

        // Imported supplies surface as raw resources to bring in instead.
        Assert.True(import.TotalRawResources.ContainsKey("Food ration"));
        Assert.True(import.TotalRawResources.ContainsKey("Medical supply"));

        // Importing means no food/medical workers, so the worker total is lower.
        Assert.True(import.TotalWorkers < produce.TotalWorkers);
    }

    [Fact]
    public void Workforce_WithoutPseudoRecipe_UsesBuiltInBasketByWareId()
    {
        // Scanned-style data: no "Workforce" pseudo-recipe, but food/medical wares carry ware ids.
        var repo = WareRepository.FromWareMaps(new WareMaps
        {
            RecipeMap = new()
            {
                ["Widget"] = new()
                {
                    ["Common"] = new RecipeData { Amount = 10, WorkforceMultiplier = 1.0, WorkforceCapacity = 100 },
                },
                ["Food Rations"] = new() { ["Common"] = new RecipeData { Amount = 1000 } },
                ["Medical Supplies"] = new() { ["Common"] = new RecipeData { Amount = 1000 } },
            },
            WareIdMap = new()
            {
                ["Food Rations"] = "foodrations",
                ["Medical Supplies"] = "medicalsupplies",
            },
        });
        var calc = new ProductionCalculator(repo);

        var produce = calc.Calculate(
            [new DesiredWare("Widget", 10)],
            new ProductionOptions { WorkforceEnabled = true, WorkforceFaction = "Argon", ProduceWorkforceSupplies = true });

        Assert.Equal(100, produce.TotalWorkers);
        // Built-in basket (2.25 food + 1.35 medical per worker) pulls in both production chains.
        Assert.NotNull(produce.RequiredFactoryGroups.FirstOrDefault(g => g.WareName == "Food Rations"));
        Assert.NotNull(produce.RequiredFactoryGroups.FirstOrDefault(g => g.WareName == "Medical Supplies"));

        var import = calc.Calculate(
            [new DesiredWare("Widget", 10)],
            new ProductionOptions { WorkforceEnabled = true, WorkforceFaction = "Argon", ProduceWorkforceSupplies = false });

        Assert.Null(import.RequiredFactoryGroups.FirstOrDefault(g => g.WareName == "Food Rations"));
        Assert.Equal(225, import.TotalRawResources["Food Rations"], 3);  // 2.25 * 100 workers
        Assert.Equal(135, import.TotalRawResources["Medical Supplies"], 3); // 1.35 * 100 workers
    }
}
