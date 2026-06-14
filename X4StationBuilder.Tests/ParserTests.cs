using System.Xml.Linq;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services.Parsing;

namespace X4StationBuilder.Tests;

public class ParserTests
{
    private static XmlLibraryMerger.MergedElement Element(string xml, string? source = null) =>
        new(XElement.Parse(xml), source);

    [Fact]
    public void ModuleParser_ResolvesMacroKindAndProducedWare()
    {
        var modules = new[]
        {
            Element(
                "<module id=\"prod_gen_energycells\" group=\"prod_gen_energycells\">" +
                "<category ware=\"energycells\" tags=\"[production, module]\" " +
                "race=\"[argon, teladi]\" faction=\"[argon, teladi]\" /></module>"),
            Element(
                "<module id=\"hab_arg_s_01\" group=\"hab_arg_s\"><category ware=\"\" /></module>",
                source: "ego_dlc_boron"),
        };
        var groups = new[]
        {
            Element("<group name=\"prod_gen_energycells\"><select macro=\"prod_gen_energycells_macro\" /></group>"),
            Element("<group name=\"hab_arg_s\"><select macro=\"hab_arg_s_01_macro\" /></group>"),
        };

        var result = ModuleParser.Parse(modules, groups);

        var prod = result.Single(m => m.Id == "prod_gen_energycells");
        Assert.Equal(ModuleKind.Production, prod.Kind);
        Assert.Equal("prod_gen_energycells_macro", prod.Macro);
        Assert.Equal("energycells", prod.ProducedWare);
        Assert.Equal(new[] { "argon", "teladi" }, prod.Factions);
        Assert.Null(prod.SourceExtension);

        var hab = result.Single(m => m.Id == "hab_arg_s_01");
        Assert.Equal(ModuleKind.Habitat, hab.Kind);
        Assert.Equal("hab_arg_s_01_macro", hab.Macro);
        Assert.Equal("ego_dlc_boron", hab.SourceExtension);
    }

    [Fact]
    public void ModuleParser_AppliesMacroWorkforceCapacity()
    {
        var modules = new[]
        {
            Element(
                "<module id=\"prod_gen_energycells\" group=\"prod_gen_energycells\">" +
                "<category ware=\"energycells\" /></module>"),
            Element("<module id=\"hab_arg_l\" group=\"hab_arg_l\"><category ware=\"\" /></module>"),
        };
        var groups = new[]
        {
            Element("<group name=\"prod_gen_energycells\"><select macro=\"prod_gen_energycells_macro\" /></group>"),
            Element("<group name=\"hab_arg_l\"><select macro=\"hab_arg_l_01_macro\" /></group>"),
        };

        var workforce = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["prod_gen_energycells_macro"] = 90,
            ["hab_arg_l_01_macro"] = 1000,
        };

        var result = ModuleParser.Parse(modules, groups, workforce);

        Assert.Equal(90, result.Single(m => m.Id == "prod_gen_energycells").WorkforceCapacity);
        Assert.Equal(1000, result.Single(m => m.Id == "hab_arg_l").WorkforceCapacity);
    }

    [Fact]
    public void ModuleParser_AppliesMacroCargoCapacity()
    {
        var modules = new[]
        {
            Element(
                "<module id=\"stor_arg_l_solid\" group=\"stor_arg_l_solid\">" +
                "<category ware=\"\" /></module>"),
        };
        var groups = new[]
        {
            Element("<group name=\"stor_arg_l_solid\"><select macro=\"storage_arg_l_solid_01_macro\" /></group>"),
        };

        var cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["storage_arg_l_solid_01_macro"] = 48000,
        };

        var result = ModuleParser.Parse(modules, groups, macroCargo: cargo);

        var storage = result.Single(m => m.Id == "stor_arg_l_solid");
        Assert.Equal(48000, storage.CargoCapacity);
        Assert.Equal(CargoType.Solid, storage.CargoType);
    }

    [Fact]
    public void ModuleParser_WithoutWorkforce_DefaultsToZero()
    {
        var modules = new[]
        {
            Element("<module id=\"prod_gen_energycells\" group=\"g\"><category ware=\"energycells\" /></module>"),
        };
        var groups = new[]
        {
            Element("<group name=\"g\"><select macro=\"prod_gen_energycells_macro\" /></group>"),
        };

        var result = ModuleParser.Parse(modules, groups);

        Assert.Equal(0, result.Single().WorkforceCapacity);
    }

    [Fact]
    public void ModuleParser_ExpandsMultiSelectGroupIntoVariants()
    {
        // An Argon harbor pier group offers three dock-layout variants under one group.
        var modules = new[]
        {
            Element(
                "<module id=\"pier_base_arg\" group=\"pier_base_arg\">" +
                "<category ware=\"\" faction=\"[argon]\" race=\"[argon]\" /></module>"),
        };
        var groups = new[]
        {
            Element(
                "<group name=\"pier_base_arg\">" +
                "<select macro=\"pier_arg_harbor_01_macro\" />" +
                "<select macro=\"pier_arg_harbor_03_macro\" />" +
                "<select macro=\"pier_arg_harbor_04_macro\" /></group>"),
        };

        var result = ModuleParser.Parse(modules, groups);

        var macros = result.Where(m => m.Kind == ModuleKind.Pier).Select(m => m.Macro).ToList();
        Assert.Equal(3, macros.Count);
        Assert.Contains("pier_arg_harbor_01_macro", macros);
        Assert.Contains("pier_arg_harbor_03_macro", macros);
        Assert.Contains("pier_arg_harbor_04_macro", macros);

        // First variant keeps the base id; extras get unique derived ids.
        Assert.Equal(result.Select(m => m.Id).Count(), result.Select(m => m.Id).Distinct().Count());
        Assert.Equal("pier_arg_harbor_01_macro", result.Single(m => m.Id == "pier_base_arg").Macro);
        // Faction/category metadata is carried onto every variant.
        Assert.All(result, m => Assert.Equal(new[] { "argon" }, m.Factions));
    }

    [Fact]
    public void WareParser_ResolvesNamesFactionsAndIngredients()
    {
        var localization = new LocalizationTable();
        localization.LoadXml(
            "<language><page id=\"20201\">" +
            "<t id=\"101\">Energy Cells</t>" +
            "<t id=\"201\">Hull Parts</t>" +
            "</page></language>");

        var wares = new[]
        {
            Element(
                "<ware id=\"energycells\" name=\"{20201,101}\" group=\"energy\">" +
                "<production amount=\"12000\" method=\"default\">" +
                "<effects><effect type=\"work\" product=\"0.25\" /></effects>" +
                "</production></ware>"),
            Element(
                "<ware id=\"hullparts\" name=\"{20201,201}\" group=\"intermediate\">" +
                "<production amount=\"86\" method=\"argon\">" +
                "<primary><ware ware=\"energycells\" amount=\"80\" /></primary>" +
                "</production></ware>"),
        };

        var maps = WareParser.Parse(wares, localization);

        Assert.True(maps.RecipeMap.ContainsKey("Energy Cells"));
        var energy = maps.RecipeMap["Energy Cells"]["Common"];
        Assert.Equal(12000, energy.Amount);
        Assert.Equal(1.25, energy.WorkforceMultiplier, 3);

        // Ingredient keys are resolved to the same display name used as the ware key.
        var hull = maps.RecipeMap["Hull Parts"]["Argon"];
        Assert.Equal(80, hull.Ingredients["Energy Cells"]);

        Assert.Equal("energy", maps.ItemTypeMap["Energy Cells"]);
        Assert.Equal("Common", maps.ItemFactionMap["Energy Cells"]);
    }

    [Fact]
    public void WareParser_NormalisesPerCycleAmountsToPerHourUsingCycleTime()
    {
        var localization = new LocalizationTable();
        localization.LoadXml(
            "<language><page id=\"20201\">" +
            "<t id=\"101\">Energy Cells</t><t id=\"201\">Microchips</t></page></language>");

        var wares = new[]
        {
            // 175 per 60s cycle → 175 * (3600/60) = 10500 per hour.
            Element(
                "<ware id=\"energycells\" name=\"{20201,101}\" group=\"energy\">" +
                "<production time=\"60\" amount=\"175\" method=\"default\" /></ware>"),
            // 72 per 600s cycle → 432/hr; ingredient 20 per cycle → 120/hr.
            Element(
                "<ware id=\"microchips\" name=\"{20201,201}\" group=\"tech\">" +
                "<production time=\"600\" amount=\"72\" method=\"default\">" +
                "<primary><ware ware=\"energycells\" amount=\"20\" /></primary>" +
                "</production></ware>"),
        };

        var maps = WareParser.Parse(wares, localization);

        Assert.Equal(10500, maps.RecipeMap["Energy Cells"]["Common"].Amount);
        var microchips = maps.RecipeMap["Microchips"]["Common"];
        Assert.Equal(432, microchips.Amount);
        Assert.Equal(120, microchips.Ingredients["Energy Cells"], 3);
    }

    [Fact]
    public void WareParser_ExcludesStationModuleWares()
    {
        var localization = new LocalizationTable();
        localization.LoadXml(
            "<language><page id=\"20201\">" +
            "<t id=\"101\">Advanced Electronics</t></page></language>");

        var wares = new[]
        {
            Element(
                "<ware id=\"advancedelectronics\" name=\"{20201,101}\" group=\"shiptech\" " +
                "tags=\"container economy stationbuilding\">" +
                "<production time=\"720\" amount=\"54\" method=\"default\" /></ware>"),
            // The buildable station module is itself a ware tagged "module"; not a commodity.
            Element(
                "<ware id=\"module_gen_prod_advancedelectronics_01\" name=\"{20201,101}\" tags=\"module\">" +
                "<production time=\"832\" amount=\"1\" method=\"default\">" +
                "<primary><ware ware=\"hullparts\" amount=\"1767\" /></primary>" +
                "</production></ware>"),
        };

        var maps = WareParser.Parse(wares, localization);

        Assert.True(maps.RecipeMap.ContainsKey("Advanced Electronics"));
        Assert.False(maps.RecipeMap.ContainsKey("module_gen_prod_advancedelectronics_01"));
        Assert.DoesNotContain(maps.WareIdMap.Values, v => v.StartsWith("module_", StringComparison.Ordinal));
    }

    [Fact]
    public void WareParser_ReadsTransportAndVolume()
    {
        var localization = new LocalizationTable();
        localization.LoadXml(
            "<language><page id=\"20201\">" +
            "<t id=\"101\">Ore</t><t id=\"201\">Hydrogen</t><t id=\"301\">Hull Parts</t>" +
            "</page></language>");

        var wares = new[]
        {
            Element("<ware id=\"ore\" name=\"{20201,101}\" group=\"minerals\" transport=\"solid\" volume=\"10\" />"),
            Element("<ware id=\"hydrogen\" name=\"{20201,201}\" group=\"gases\" transport=\"liquid\" volume=\"6\" />"),
            Element(
                "<ware id=\"hullparts\" name=\"{20201,301}\" group=\"intermediate\" transport=\"container\" volume=\"8\">" +
                "<production amount=\"86\" method=\"argon\" /></ware>"),
        };

        var maps = WareParser.Parse(wares, localization);

        Assert.Equal("solid", maps.WareTransportMap["Ore"]);
        Assert.Equal(10, maps.WareVolumeMap["Ore"]);
        Assert.Equal("liquid", maps.WareTransportMap["Hydrogen"]);
        Assert.Equal(6, maps.WareVolumeMap["Hydrogen"]);
        Assert.Equal("container", maps.WareTransportMap["Hull Parts"]);
        Assert.Equal(8, maps.WareVolumeMap["Hull Parts"]);
    }

    [Fact]
    public void LocalizationTable_ExpandsNestedReferences()
    {
        var table = new LocalizationTable();
        table.LoadXml(
            "<language><page id=\"1\">" +
            "<t id=\"1\">Argon</t>" +
            "<t id=\"2\">{1,1} Federation</t>" +
            "</page></language>");

        Assert.Equal("Argon Federation", table.Resolve("{1,2}"));
    }
}
