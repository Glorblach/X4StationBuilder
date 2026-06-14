using X4StationBuilder.Core.Data;

namespace X4StationBuilder.Tests;

public class WareRepositoryTests
{
    private static readonly WareRepository Repository = new();

    [Fact]
    public void AntimatterCell_LoadsWithCorrectRecipe()
    {
        var ware = Repository.GetByName("Antimatter cell");

        Assert.NotNull(ware);
        Assert.True(ware!.RecipesByFaction.ContainsKey("Common"));

        var recipe = ware.RecipesByFaction["Common"];
        Assert.Equal(3300, recipe.Amount);
        Assert.Equal(1.21, recipe.WorkforceMultiplier, 3);
        Assert.Equal(120, recipe.WorkforceCapacity);
        Assert.Equal(3000, recipe.Ingredients["Energy cell"]);
        Assert.Equal(9600, recipe.Ingredients["Hydrogen"]);
    }

    [Fact]
    public void AllWares_IsNotEmpty()
    {
        Assert.NotEmpty(Repository.AllWares);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        Assert.NotNull(Repository.GetByName("antimatter cell"));
        Assert.True(Repository.TryGet("ENERGY CELL", out var ware));
        Assert.Equal("Energy cell", ware.Name);
    }

    [Fact]
    public void LeafResource_IsLookupable_ButNotProducible()
    {
        var hydrogen = Repository.GetByName("Hydrogen");

        Assert.NotNull(hydrogen);
        Assert.False(hydrogen!.IsProducible);
        Assert.Empty(hydrogen.RecipesByFaction);
    }

    [Fact]
    public void CategorySortOrder_IsLoaded()
    {
        Assert.Equal(1, Repository.CategorySortIndex("RefinedResource"));
        Assert.Equal(7, Repository.CategorySortIndex("TechProduct"));
    }

    [Fact]
    public void WorkforcePseudoRecipe_PreservesStarPrefixedIngredients()
    {
        var workforce = Repository.GetByName("Workforce");

        Assert.NotNull(workforce);
        var recipe = workforce!.RecipesByFaction["Argon"];
        Assert.True(recipe.Ingredients.ContainsKey("*Food ration"));
        Assert.Equal(1.125, recipe.Ingredients["*Food ration"], 4);
        Assert.Equal(0.675, recipe.Ingredients["*Medical supply"], 4);
    }
}
