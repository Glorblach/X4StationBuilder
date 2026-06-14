using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "x4sb-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void RoundTrips_PrefixAndDefaultDocks()
    {
        var store = new SettingsStore(_path);
        var settings = new AppSettings
        {
            GamePath = @"C:\X4",
            StationNamePrefix = "NML-ARG",
            DefaultDocks =
            [
                new DockDefault { Macro = "dockarea_arg_m_station_01_macro", Count = 1 },
                new DockDefault { Macro = "pier_arg_harbor_03_macro", Count = 4 },
            ],
        };

        store.Save(settings);
        var loaded = new SettingsStore(_path).Load();

        Assert.Equal("C:\\X4", loaded.GamePath);
        Assert.Equal("NML-ARG", loaded.StationNamePrefix);
        Assert.Equal(2, loaded.DefaultDocks.Count);
        Assert.Equal("dockarea_arg_m_station_01_macro", loaded.DefaultDocks[0].Macro);
        Assert.Equal(1, loaded.DefaultDocks[0].Count);
        Assert.Equal("pier_arg_harbor_03_macro", loaded.DefaultDocks[1].Macro);
        Assert.Equal(4, loaded.DefaultDocks[1].Count);
    }

    [Fact]
    public void Defaults_AreEmptyWhenFileMissing()
    {
        var loaded = new SettingsStore(_path).Load();

        Assert.Null(loaded.StationNamePrefix);
        Assert.Empty(loaded.DefaultDocks);
    }
}
