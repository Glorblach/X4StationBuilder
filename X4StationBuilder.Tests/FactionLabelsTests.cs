using X4StationBuilder.App.ViewModels;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Tests;

public class FactionLabelsTests
{
    [Theory]
    [InlineData("argon", "Argon")]
    [InlineData("arg", "Argon")]
    [InlineData("ter", "Terran")]
    [InlineData("boron", "Boron")]
    [InlineData("xen", "Xenon")]
    public void ToDisplay_MapsKnownTokens(string token, string expected) =>
        Assert.Equal(expected, FactionLabels.ToDisplay(token));

    [Theory]
    [InlineData("gen")]
    [InlineData("")]
    [InlineData(null)]
    public void ToDisplay_ReturnsNull_ForUnknownOrGeneric(string? token) =>
        Assert.Null(FactionLabels.ToDisplay(token));

    [Fact]
    public void Prefix_AddsFaction_WhenAbsent() =>
        Assert.Equal("Terran Energy Cell Production",
            FactionLabels.Prefix("Energy Cell Production", "Terran"));

    [Fact]
    public void Prefix_DoesNotDouble_WhenAlreadyPresent() =>
        Assert.Equal("Terran Energy Cell Production",
            FactionLabels.Prefix("Terran Energy Cell Production", "Terran"));

    [Fact]
    public void Prefix_NoOps_WhenFactionNull() =>
        Assert.Equal("Energy Cell Production",
            FactionLabels.Prefix("Energy Cell Production", null));

    [Fact]
    public void ForModule_PrefersMacroToken()
    {
        var module = new StationModule
        {
            Id = "prod_ter_energycells",
            Macro = "prod_ter_energycells_01_macro",
            Factions = ["argon"], // macro token should win over this
        };

        Assert.Equal("Terran", FactionLabels.ForModule(module));
    }

    [Fact]
    public void ForModule_FallsBackToFactions_WhenMacroGeneric()
    {
        var module = new StationModule
        {
            Id = "buildmodule_gen_ship_l",
            Macro = "buildmodule_gen_ship_l_01_macro",
            Factions = ["teladi"],
        };

        Assert.Equal("Teladi", FactionLabels.ForModule(module));
    }
}
