using System.Text;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services.Archive;
using X4StationBuilder.Core.Services.Parsing;

namespace X4StationBuilder.Tests;

public class MacroGeometryReaderTests : IDisposable
{
    private readonly string _root;

    public MacroGeometryReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "x4sb-geom-" + Guid.NewGuid().ToString("N"));
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
    public void Read_ParsesSnapsExclusionAndFootprint_SkippingIrrelevantConnections()
    {
        // A leading UTF-8 BOM on the component file exercises the BOM-stripping path.
        const string bom = "\uFEFF";
        var component = bom +
            "<components><component name=\"test_comp\"><connections>" +
            "<connection name=\"ConnectionSnap001\" tags=\"snap \">" +
            "  <offset><position x=\"0\" y=\"0\" z=\"400\"/></offset></connection>" +
            "<connection name=\"ConnectionSnap002\" tags=\"snap \">" +
            "  <offset><position x=\"0\" y=\"0\" z=\"-400\"/>" +
            "  <quaternion qx=\"0\" qy=\"-1\" qz=\"0\" qw=\"0\"/></offset></connection>" +
            "<connection name=\"Con_exclusionzone001\" tags=\"exclusionzone ship_m\">" +
            "  <offset><position x=\"-2000\" y=\"0\" z=\"0\"/></offset></connection>" +
            "<connection name=\"con_turret_001\" tags=\"turret medium\">" +
            "  <offset><position x=\"99\" y=\"99\" z=\"99\"/></offset></connection>" +
            "</connections></component></components>";

        WriteArchive(
            Path.Combine(_root, "01.cat"),
            ("index/macros.xml", "<macros><entry name=\"test_macro\" value=\"assets/test_macro\"/></macros>"),
            ("index/components.xml", "<components><entry name=\"test_comp\" value=\"assets/test_comp\"/></components>"),
            ("assets/test_macro.xml", "<macros><macro name=\"test_macro\"><component ref=\"test_comp\"/></macro></macros>"),
            ("assets/test_comp.xml", component));

        var fs = X4FileSystem.Mount(_root);

        var result = MacroGeometryReader.Read(fs, new[] { "test_macro" });

        Assert.True(result.ContainsKey("test_macro"));
        var geometry = result["test_macro"];

        // Two snaps parsed; the turret/part connection is dropped.
        Assert.Equal(2, geometry.Snaps.Count());
        Assert.Single(geometry.ExclusionZones);
        Assert.DoesNotContain(geometry.Connections, c => c.Name == "con_turret_001");

        // Footprint half-extent on Z is the snap distance; principal direction derived from position.
        Assert.Equal(400, geometry.HalfExtents.Z, 3);
        var plusZ = geometry.Snaps.Single(s => s.Name == "ConnectionSnap001");
        Assert.Equal(new Vec3(0, 0, 1), plusZ.Direction);

        var exclusion = geometry.ExclusionZones.Single();
        Assert.Contains("ship_m", exclusion.Tags);
        Assert.Equal(-2000, exclusion.Position.X, 3);
    }

    [Fact]
    public void Read_ReturnsEmpty_ForUnknownMacro()
    {
        WriteArchive(
            Path.Combine(_root, "01.cat"),
            ("index/macros.xml", "<macros></macros>"),
            ("index/components.xml", "<components></components>"));

        var fs = X4FileSystem.Mount(_root);

        var result = MacroGeometryReader.Read(fs, new[] { "missing_macro" });

        Assert.Empty(result);
    }
}
