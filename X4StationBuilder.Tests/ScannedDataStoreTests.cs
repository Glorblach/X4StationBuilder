using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class ScannedDataStoreTests : IDisposable
{
    private readonly string _dir;

    public ScannedDataStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "x4sb-store-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static ScanResult SampleResult()
    {
        var maps = new WareMaps
        {
            RecipeMap =
            {
                ["Energy Cells"] = new()
                {
                    ["Common"] = new RecipeData { Amount = 12000, WorkforceMultiplier = 1.25 },
                },
            },
            ItemFactionMap = { ["Energy Cells"] = "Common" },
            ItemTypeMap = { ["Energy Cells"] = "energy" },
        };

        return new ScanResult
        {
            Metadata = new ScanMetadata { GamePath = @"X:\X4", ModuleCount = 1, WareCount = 1 },
            Modules = new[] { new StationModule { Id = "prod_gen_energycells", Kind = ModuleKind.Production } },
            Wares = maps,
        };
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new ScannedDataStore(_dir);
        Assert.False(store.HasScannedData);

        store.Save(SampleResult());

        Assert.True(store.HasScannedData);
        var modules = store.LoadModules();
        Assert.NotNull(modules);
        Assert.Equal("prod_gen_energycells", modules!.Single().Id);

        var maps = store.LoadWareMaps();
        Assert.NotNull(maps);
        Assert.Equal(12000, maps!.RecipeMap["Energy Cells"]["Common"].Amount);

        Assert.Equal(@"X:\X4", store.LoadMetadata()!.GamePath);
    }

    [Fact]
    public void LoadReturnsNullWhenNoData()
    {
        var store = new ScannedDataStore(_dir);
        Assert.Null(store.LoadWareMaps());
        Assert.Null(store.LoadModules());
        Assert.Null(store.LoadMetadata());
    }

    [Fact]
    public void WareRepository_PrefersScannedDataWhenPresent()
    {
        var store = new ScannedDataStore(_dir);
        store.Save(SampleResult());

        var repo = WareRepository.CreatePreferringScanned(store);

        // The scanned dataset uses plural localized names, unlike the bundled maps.
        Assert.NotNull(repo.GetByName("Energy Cells"));
    }

    [Fact]
    public void WareRepository_FallsBackToBundledMapsWhenNoScan()
    {
        var store = new ScannedDataStore(_dir);

        var repo = WareRepository.CreatePreferringScanned(store);

        // Bundled maps use singular names.
        Assert.NotNull(repo.GetByName("Energy cell"));
    }
}
