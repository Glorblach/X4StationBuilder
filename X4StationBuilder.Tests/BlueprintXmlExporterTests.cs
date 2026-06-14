using System.Globalization;
using System.Xml.Linq;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class BlueprintXmlExporterTests
{
    private static StationModule Module(string id, string macro, string? sourceExtension = null) => new()
    {
        Id = id,
        Name = id,
        Macro = macro,
        SourceExtension = sourceExtension,
    };

    private static PlacedModule Root() => new()
    {
        Index = 1,
        Module = Module("conn", "struct_arg_cross_01_macro"),
        Position = Vec3.Zero,
        Rotation = Rotation.None,
    };

    private static LayoutResult Layout(params PlacedModule[] modules) => new()
    {
        Modules = modules,
        BoundingBoxSize = new Vec3(600, 600, 600),
    };

    private static BlueprintXmlExporter.ExportOptions Options(
        string name = "Test Plan",
        IReadOnlyList<DlcInfo>? dlcs = null) => new()
    {
        PlanName = name,
        PlanId = "player_1234567890",
        Dlcs = dlcs,
    };

    [Fact]
    public void Export_ProducesValidParseableXml()
    {
        var layout = Layout(
            Root(),
            new PlacedModule
            {
                Index = 2,
                Module = Module("prod", "prod_arg_energycells_01_macro"),
                Position = new Vec3(600, 0, 0),
                Rotation = Rotation.None,
                PredecessorIndex = 1,
                PredecessorConnection = "ConnectionSnap004",
                Connection = "ConnectionSnap002",
            });

        var xml = new BlueprintXmlExporter().ExportToString(layout, Options());
        var doc = XDocument.Parse(xml);

        var plan = doc.Root!.Element("plan")!;
        Assert.Equal("plans", doc.Root!.Name.LocalName);
        Assert.Equal("player_1234567890", plan.Attribute("id")!.Value);
        Assert.Equal("Test Plan", plan.Attribute("name")!.Value);
        Assert.Equal("", plan.Attribute("description")!.Value);
        Assert.Equal(2, plan.Elements("entry").Count());
    }

    [Fact]
    public void Export_IndexesAreSequentialAndMatchPlacedModules()
    {
        var layout = Layout(
            Root(),
            new PlacedModule { Index = 2, Module = Module("a", "macro_a"), Position = new Vec3(600, 0, 0) },
            new PlacedModule { Index = 3, Module = Module("b", "macro_b"), Position = new Vec3(-600, 0, 0) });

        var doc = new BlueprintXmlExporter().BuildDocument(layout, Options());

        var indices = doc.Root!.Element("plan")!
            .Elements("entry")
            .Select(e => int.Parse(e.Attribute("index")!.Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal([1, 2, 3], indices);
    }

    [Fact]
    public void Export_OmitsZeroPositionAxes()
    {
        var layout = Layout(new PlacedModule
        {
            Index = 1,
            Module = Module("a", "macro_a"),
            Position = new Vec3(600, 0, -400),
        });

        var position = new BlueprintXmlExporter().BuildDocument(layout, Options())
            .Root!.Element("plan")!.Element("entry")!.Element("offset")!.Element("position")!;

        Assert.Equal("600", position.Attribute("x")!.Value);
        Assert.Null(position.Attribute("y"));
        Assert.Equal("-400", position.Attribute("z")!.Value);
    }

    [Fact]
    public void Export_OmitsRotationWhenAllZero()
    {
        var layout = Layout(new PlacedModule
        {
            Index = 1,
            Module = Module("a", "macro_a"),
            Position = new Vec3(600, 0, 0),
            Rotation = Rotation.None,
        });

        var offset = new BlueprintXmlExporter().BuildDocument(layout, Options())
            .Root!.Element("plan")!.Element("entry")!.Element("offset")!;

        Assert.Null(offset.Element("rotation"));
    }

    [Fact]
    public void Export_EmitsOnlyNonZeroRotationAxes()
    {
        var layout = Layout(new PlacedModule
        {
            Index = 1,
            Module = Module("a", "macro_a"),
            Position = Vec3.Zero,
            Rotation = new Rotation(Yaw: 45),
        });

        var rotation = new BlueprintXmlExporter().BuildDocument(layout, Options())
            .Root!.Element("plan")!.Element("entry")!.Element("offset")!.Element("rotation")!;

        Assert.Equal("45", rotation.Attribute("yaw")!.Value);
        Assert.Null(rotation.Attribute("pitch"));
        Assert.Null(rotation.Attribute("roll"));
    }

    [Fact]
    public void Export_EmitsConnectionAndPredecessorOnlyWhenSet()
    {
        var layout = Layout(
            Root(),
            new PlacedModule
            {
                Index = 2,
                Module = Module("a", "macro_a"),
                Position = new Vec3(600, 0, 0),
                PredecessorIndex = 1,
                PredecessorConnection = "ConnectionSnap004",
                Connection = "ConnectionSnap002",
            });

        var entries = new BlueprintXmlExporter().BuildDocument(layout, Options())
            .Root!.Element("plan")!.Elements("entry").ToList();

        // Root: no connection / predecessor.
        Assert.Null(entries[0].Attribute("connection"));
        Assert.Null(entries[0].Element("predecessor"));

        // Connected: lower-cased connection + predecessor.
        Assert.Equal("connectionsnap002", entries[1].Attribute("connection")!.Value);
        var predecessor = entries[1].Element("predecessor")!;
        Assert.Equal("1", predecessor.Attribute("index")!.Value);
        Assert.Equal("connectionsnap004", predecessor.Attribute("connection")!.Value);
    }

    [Fact]
    public void Export_EmitsPatchesOnlyForDlcSourcedModules()
    {
        var layout = Layout(
            Root(),
            new PlacedModule
            {
                Index = 2,
                Module = Module("terran", "prod_ter_energycells_01_macro", "ego_dlc_terran"),
                Position = new Vec3(600, 0, 0),
            });

        var dlcs = new List<DlcInfo>
        {
            new() { Id = "ego_dlc_terran", Name = "Cradle of Humanity", Version = "700" },
            new() { Id = "ego_dlc_boron", Name = "Kingdom End", Version = "710" },
        };

        var plan = new BlueprintXmlExporter().BuildDocument(layout, Options(dlcs: dlcs))
            .Root!.Element("plan")!;

        var patches = plan.Element("patches")!;
        var patch = Assert.Single(patches.Elements("patch"));
        Assert.Equal("ego_dlc_terran", patch.Attribute("extension")!.Value);
        Assert.Equal("700", patch.Attribute("version")!.Value);
        Assert.Equal("Cradle of Humanity", patch.Attribute("name")!.Value);
    }

    [Fact]
    public void Export_OmitsPatchesForBaseGameOnlyLayout()
    {
        var layout = Layout(
            Root(),
            new PlacedModule
            {
                Index = 2,
                Module = Module("arg", "prod_arg_energycells_01_macro"),
                Position = new Vec3(600, 0, 0),
            });

        var plan = new BlueprintXmlExporter().BuildDocument(layout, Options())
            .Root!.Element("plan")!;

        Assert.Null(plan.Element("patches"));
    }

    [Fact]
    public void GeneratedPlanId_UsesPlayerEpochFormat()
    {
        var layout = Layout(Root());
        var options = new BlueprintXmlExporter.ExportOptions { PlanName = "X" };

        var id = new BlueprintXmlExporter().BuildDocument(layout, options)
            .Root!.Element("plan")!.Attribute("id")!.Value;

        Assert.StartsWith("player_", id);
        Assert.True(long.TryParse(id["player_".Length..], out _));
    }

    [Fact]
    public void ExportToFile_WritesParseableFile()
    {
        var layout = Layout(Root());
        var path = Path.Combine(Path.GetTempPath(), $"x4sb_{Guid.NewGuid():N}.xml");
        try
        {
            new BlueprintXmlExporter().ExportToFile(layout, Options(), path);
            Assert.True(File.Exists(path));
            var doc = XDocument.Load(path);
            Assert.Equal("plans", doc.Root!.Name.LocalName);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
