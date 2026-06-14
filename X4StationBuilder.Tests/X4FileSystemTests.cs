using System.Text;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Tests;

public class X4FileSystemTests : IDisposable
{
    private readonly string _root;

    public X4FileSystemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "x4sb-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void WriteArchive(string catPath, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(catPath)!);
        var datPath = Path.ChangeExtension(catPath, ".dat");

        var blob = new StringBuilder();
        var lines = new List<string>();
        foreach (var (path, content) in files)
        {
            var bytes = Encoding.UTF8.GetByteCount(content);
            lines.Add($"{path} {bytes} 1 hash");
            blob.Append(content);
        }

        File.WriteAllText(datPath, blob.ToString());
        File.WriteAllLines(catPath, lines);
    }

    [Fact]
    public void ExtensionEntryOverridesBaseForSamePath()
    {
        WriteArchive(Path.Combine(_root, "01.cat"), ("libraries/modules.xml", "BASE"));
        WriteArchive(
            Path.Combine(_root, "extensions", "ego_dlc_test", "ext_01.cat"),
            ("libraries/modules.xml", "EXT"));

        var fs = X4FileSystem.Mount(_root);

        // Highest-precedence wins.
        Assert.Equal("EXT", CatDatReader.ReadText(fs.Get("libraries/modules.xml")!));

        // GetAll preserves mount order: base first, then extension overlay.
        var all = fs.GetAll("libraries/modules.xml");
        Assert.Equal(2, all.Count);
        Assert.Equal("BASE", CatDatReader.ReadText(all[0]));
        Assert.Equal("EXT", CatDatReader.ReadText(all[1]));
    }

    [Fact]
    public void IgnoresSignatureArchives()
    {
        WriteArchive(Path.Combine(_root, "01.cat"), ("a.xml", "A"));
        WriteArchive(Path.Combine(_root, "01_sig.cat"), ("a.xml", "SHOULD_BE_IGNORED"));

        var fs = X4FileSystem.Mount(_root);

        Assert.Single(fs.GetAll("a.xml"));
        Assert.Equal("A", CatDatReader.ReadText(fs.Get("a.xml")!));
    }
}
