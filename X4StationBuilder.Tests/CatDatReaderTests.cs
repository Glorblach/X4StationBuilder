using System.Text;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Tests;

public class CatDatReaderTests : IDisposable
{
    private readonly string _dir;

    public CatDatReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "x4sb-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void ParsesEntriesWithRunningOffsets()
    {
        var lines = new[]
        {
            "libraries/wares.xml 5 100 aaa",
            "with space/file.xml 3 200 bbb",
        };

        var reader = CatDatReader.FromCatLines(lines, Path.Combine(_dir, "x.dat"));

        Assert.Equal(2, reader.Entries.Count);
        Assert.Equal("libraries/wares.xml", reader.Entries[0].Path);
        Assert.Equal(5, reader.Entries[0].Size);
        Assert.Equal(0, reader.Entries[0].Offset);

        // Path containing a space is preserved; offset follows the previous size.
        Assert.Equal("with space/file.xml", reader.Entries[1].Path);
        Assert.Equal(3, reader.Entries[1].Size);
        Assert.Equal(5, reader.Entries[1].Offset);
    }

    [Fact]
    public void ReadsBytesAndTextFromDatByOffset()
    {
        var catPath = Path.Combine(_dir, "01.cat");
        var datPath = Path.Combine(_dir, "01.dat");

        var first = "HELLO";
        var second = "WORLD!";
        File.WriteAllBytes(datPath, Encoding.UTF8.GetBytes(first + second));
        File.WriteAllLines(catPath, new[]
        {
            $"a.txt {first.Length} 1 h1",
            $"b.txt {second.Length} 2 h2",
        });

        var reader = CatDatReader.FromCatFile(catPath);

        Assert.Equal("HELLO", CatDatReader.ReadText(reader.Entries[0]));
        Assert.Equal("WORLD!", CatDatReader.ReadText(reader.Entries[1]));
    }
}
