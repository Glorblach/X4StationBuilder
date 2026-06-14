using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Tests;

public class WareGroupResolverTests
{
    private static readonly WareGroupResolver Resolver = new();

    private static Ware Ware(string name, string? category = null, string? wareId = null) =>
        new() { Name = name, Category = category, WareId = wareId };

    [Fact]
    public void Resolve_UsesScannedGroupId_FromCategory()
    {
        // Scanned data stores the X4 group id (e.g. "refined") in Category.
        var (name, order) = Resolver.Resolve(Ware("Whatever", category: "refined"));

        Assert.Equal("Refined Goods", name);
        Assert.Equal(3, order);
    }

    [Fact]
    public void Resolve_UsesWareId_WhenCategoryIsNotAGroupId()
    {
        var (name, _) = Resolver.Resolve(Ware("Graphene", category: "RefinedResource", wareId: "graphene"));

        Assert.Equal("Refined Goods", name);
    }

    [Theory]
    [InlineData("Energy cell", "Energy")]
    [InlineData("Antimatter cell", "Refined Goods")]
    [InlineData("Hull part", "High Tech Goods")]
    [InlineData("Medical supply", "Pharmaceutical Goods")]
    [InlineData("Advanced electronics", "Ship Technology")]
    public void Resolve_FallsBackToDisplayName_ForBundledWares(string wareName, string expectedGroup)
    {
        // Bundled wares have no ware id and a non-group Category; resolution falls back to a
        // case/spacing/plural-tolerant display-name match against the baked x4-game.com data.
        var (name, _) = Resolver.Resolve(Ware(wareName));

        Assert.Equal(expectedGroup, name);
    }

    [Fact]
    public void Resolve_ReturnsOther_ForUnknownWare()
    {
        var (name, order) = Resolver.Resolve(Ware("Totally Made Up Ware"));

        Assert.Equal(WareGroupResolver.OtherGroupName, name);
        Assert.Equal(999, order);
    }

    [Fact]
    public void GetOrder_PlacesEnergyBeforeRefinedBeforeOther()
    {
        Assert.True(Resolver.GetOrder("Energy") < Resolver.GetOrder("Refined Goods"));
        Assert.True(Resolver.GetOrder("Refined Goods") < Resolver.GetOrder(WareGroupResolver.OtherGroupName));
    }
}
