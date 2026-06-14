using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class DlcDetectorTests : IDisposable
{
    private readonly string _root;

    public DlcDetectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "x4sb-dlc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteExtension(string folder, string contentXml)
    {
        var dir = Path.Combine(_root, "extensions", folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "content.xml"), contentXml);
    }

    [Fact]
    public void ParsesContentXmlAttributes()
    {
        WriteExtension("ego_dlc_boron",
            "<?xml version=\"1.0\"?><content id=\"ego_dlc_boron\" version=\"900\" name=\"Kingdom End\" author=\"Egosoft GmbH\" />");
        WriteExtension("some_mod",
            "<?xml version=\"1.0\"?><content id=\"some_mod\" version=\"1\" name=\"A Mod\" author=\"Someone\" />");

        var dlcs = DlcDetector.Detect(_root);

        Assert.Equal(2, dlcs.Count);

        var boron = dlcs.Single(d => d.Id == "ego_dlc_boron");
        Assert.Equal("Kingdom End", boron.Name);
        Assert.Equal("900", boron.Version);
        Assert.True(boron.IsOfficialDlc);

        Assert.False(dlcs.Single(d => d.Id == "some_mod").IsOfficialDlc);
    }

    [Fact]
    public void ReturnsEmptyWhenNoExtensionsFolder()
    {
        Assert.Empty(DlcDetector.Detect(_root));
    }
}
