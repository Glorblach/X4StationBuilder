using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class StationLayoutEngineTests
{
    private static ConnectionPoint Snap(string name, Vec3 pos, Vec3 dir) => new()
    {
        Name = name,
        Kind = ConnectionKind.Snap,
        Tags = ["snap"],
        Position = pos,
        Direction = dir,
    };

    private static StationModule Cross() => new()
    {
        Id = "conn_arg",
        Name = "Cross Connector",
        Macro = "struct_arg_cross_01_macro",
        Kind = ModuleKind.Connection,
        Geometry = new ModuleGeometry
        {
            HalfExtents = new Vec3(200, 200, 200),
            Connections =
            [
                Snap("ConnectionSnap001", new Vec3(0, 0, 200), new Vec3(0, 0, 1)),
                Snap("ConnectionSnap002", new Vec3(0, 0, -200), new Vec3(0, 0, -1)),
                Snap("ConnectionSnap003", new Vec3(-200, 0, 0), new Vec3(-1, 0, 0)),
                Snap("ConnectionSnap004", new Vec3(200, 0, 0), new Vec3(1, 0, 0)),
                Snap("ConnectionSnap005", new Vec3(0, -200, 0), new Vec3(0, -1, 0)),
                Snap("ConnectionSnap006", new Vec3(0, 200, 0), new Vec3(0, 1, 0)),
            ],
        },
    };

    // A production module with snaps only on the X axis (like prod_gen_energycells, ±1200).
    private static StationModule Production(string id) => new()
    {
        Id = id,
        Name = id,
        Macro = id + "_macro",
        Kind = ModuleKind.Production,
        Size = ModuleSize.M,
        Geometry = new ModuleGeometry
        {
            HalfExtents = new Vec3(1200, 600, 600),
            Connections =
            [
                Snap("ConnectionSnap001", new Vec3(1200, 0, 0), new Vec3(1, 0, 0)),
                Snap("ConnectionSnap002", new Vec3(-1200, 0, 0), new Vec3(-1, 0, 0)),
            ],
        },
    };

    // A dock modelled on the real dockarea_arg_m: structural snaps on the four horizontal faces
    // (±X/±Z) and an upward (+Y) docking pad / exclusion zone — so a pure-yaw mount keeps the pad up.
    private static StationModule Dock() => new()
    {
        Id = "dockarea_arg_m",
        Name = "M Dock",
        Macro = "dockarea_arg_m_macro",
        Kind = ModuleKind.Dock,
        Size = ModuleSize.M,
        Geometry = new ModuleGeometry
        {
            HalfExtents = new Vec3(200, 200, 200),
            Connections =
            [
                Snap("ConnectionSnap001", new Vec3(0, 0, 200), new Vec3(0, 0, 1)),
                Snap("ConnectionSnap002", new Vec3(0, 0, -200), new Vec3(0, 0, -1)),
                Snap("ConnectionSnap003", new Vec3(200, 0, 0), new Vec3(1, 0, 0)),
                Snap("ConnectionSnap004", new Vec3(-200, 0, 0), new Vec3(-1, 0, 0)),
                new ConnectionPoint
                {
                    Name = "Con_exclusionzone001",
                    Kind = ConnectionKind.ExclusionZone,
                    Tags = ["exclusionzone", "ship_m"],
                    Position = new Vec3(0, 1800, 0),
                    Direction = new Vec3(0, 1, 0),
                },
            ],
        },
    };

    private static StationLayout SampleStation(int productionCount, int dockCount)
    {
        var bodies = new List<LayoutItem>();
        for (var i = 0; i < productionCount; i++)
        {
            bodies.Add(new LayoutItem(Production("prod_" + i), 1));
        }

        return new StationLayout
        {
            Modules = bodies,
            Docks = dockCount > 0 ? [new LayoutItem(Dock(), dockCount)] : [],
            Connectors = [Cross()],
        };
    }

    [Fact]
    public void Layout_PlacesEveryRequestedModule()
    {
        var result = new StationLayoutEngine().Layout(SampleStation(productionCount: 8, dockCount: 3));

        Assert.Equal(8, result.Modules.Count(m => m.Module.Kind == ModuleKind.Production));
        Assert.Equal(3, result.Modules.Count(m => m.Module.Kind == ModuleKind.Dock));
        Assert.Contains(result.Modules, m => m.Module.Kind == ModuleKind.Connection);
    }

    [Fact]
    public void Layout_FormsConnectedTree_WithSingleRoot()
    {
        var result = new StationLayoutEngine().Layout(SampleStation(10, 2));

        var byIndex = result.Modules.ToDictionary(m => m.Index);

        // Indices are 1..N and unique.
        Assert.Equal(
            Enumerable.Range(1, result.Modules.Count).ToHashSet(),
            byIndex.Keys.ToHashSet());

        // Exactly one root; every other module snaps to an earlier entry (a valid spanning tree).
        Assert.Single(result.Modules, m => m.PredecessorIndex is null);
        foreach (var m in result.Modules.Where(m => m.PredecessorIndex is not null))
        {
            Assert.True(m.PredecessorIndex < m.Index);
            Assert.True(m.PredecessorIndex >= 1);
            Assert.NotNull(m.Connection);
            Assert.NotNull(m.PredecessorConnection);
        }
    }

    [Fact]
    public void Layout_HasNoCoincidentModules()
    {
        var result = new StationLayoutEngine().Layout(SampleStation(12, 4));

        var positions = result.Modules
            .Select(m => (
                Math.Round(m.Position.X, 1),
                Math.Round(m.Position.Y, 1),
                Math.Round(m.Position.Z, 1)))
            .ToList();

        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void Layout_IsCentredOnOrigin()
    {
        var result = new StationLayoutEngine().Layout(SampleStation(9, 2));

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in result.Modules)
        {
            min = new Vec3(Math.Min(min.X, p.Position.X), Math.Min(min.Y, p.Position.Y), Math.Min(min.Z, p.Position.Z));
            max = new Vec3(Math.Max(max.X, p.Position.X), Math.Max(max.Y, p.Position.Y), Math.Max(max.Z, p.Position.Z));
        }

        var centreX = (min.X + max.X) / 2;
        var centreY = (min.Y + max.Y) / 2;
        var centreZ = (min.Z + max.Z) / 2;

        Assert.Equal(0, centreX, 1);
        Assert.Equal(0, centreY, 1);
        Assert.Equal(0, centreZ, 1);
    }

    [Fact]
    public void Layout_ConnectorSkeletonIsNearCube()
    {
        // Docks/piers add directional standoff spikes by design, so measure the core connector grid
        // (no docks) — that is what must stay near-cube.
        var result = new StationLayoutEngine().Layout(SampleStation(20, 0));

        var connectors = result.Modules.Where(m => m.Module.Kind == ModuleKind.Connection).ToList();
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var c in connectors)
        {
            min = new Vec3(Math.Min(min.X, c.Position.X), Math.Min(min.Y, c.Position.Y), Math.Min(min.Z, c.Position.Z));
            max = new Vec3(Math.Max(max.X, c.Position.X), Math.Max(max.Y, c.Position.Y), Math.Max(max.Z, c.Position.Z));
        }

        var dx = max.X - min.X;
        var dy = max.Y - min.Y;
        var dz = max.Z - min.Z;
        var longest = Math.Max(dx, Math.Max(dy, dz));
        var shortest = Math.Min(dx, Math.Min(dy, dz));

        // Near-cube: longest skeleton axis is at most 2 grid steps longer than the shortest.
        Assert.True(longest - shortest <= 2 * 400 + 1, $"skeleton not cube-like: {dx}x{dy}x{dz}");
    }

    [Fact]
    public void Layout_PlacesDocksOnOuterShell_FacingOutward()
    {
        var result = new StationLayoutEngine().Layout(SampleStation(8, 4));
        var byIndex = result.Modules.ToDictionary(m => m.Index);

        foreach (var dock in result.Modules.Where(m => m.Module.Kind == ModuleKind.Dock))
        {
            // Dock mounts at the end of its standoff chain — beyond the connector it attaches to.
            var predecessor = byIndex[dock.PredecessorIndex!.Value];
            Assert.True(dock.Position.Length >= predecessor.Position.Length - 1,
                $"dock not beyond its mount connector: {dock.Position.Length} < {predecessor.Position.Length}");

            // Its docking exclusion zone points further out than the dock itself (corridor faces space).
            var excl = dock.Module.Geometry!.ExclusionZones.Single();
            var exclWorld = WorldPoint(dock, excl.Position);
            Assert.True(exclWorld.Length > dock.Position.Length + 1,
                "dock exclusion zone does not extend outward from the station");
        }
    }

    [Fact]
    public void Layout_FallsBackToPlaceholderGeometry_WhenModuleHasNone()
    {
        var noGeometry = new StationModule
        {
            Id = "prod_seed",
            Macro = "prod_seed_macro",
            Kind = ModuleKind.Production,
            Size = ModuleSize.L,
        };

        var layout = new StationLayout
        {
            Modules = [new LayoutItem(noGeometry, 4)],
            Docks = [],
            Connectors = [], // forces the synthetic connector too
        };

        var result = new StationLayoutEngine().Layout(layout);

        Assert.Equal(4, result.Modules.Count(m => m.Module.Kind == ModuleKind.Production));
        Assert.All(result.Modules.Where(m => m.PredecessorIndex is not null),
            m => Assert.NotNull(m.Connection));
    }

    // Mirrors the engine's snap-mating transform so tests can verify outward orientation.
    private static Vec3 WorldPoint(PlacedModule m, Vec3 local)
    {
        var q = QuatFromEuler(m.Rotation);
        var r = System.Numerics.Vector3.Transform(new System.Numerics.Vector3((float)local.X, (float)local.Y, (float)local.Z), q);
        return new Vec3(m.Position.X + r.X, m.Position.Y + r.Y, m.Position.Z + r.Z);
    }

    private static System.Numerics.Quaternion QuatFromEuler(Rotation rot)
    {
        float Deg(double d) => (float)(d * Math.PI / 180.0);
        var yaw = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, Deg(rot.Yaw));
        var pitch = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, Deg(rot.Pitch));
        var roll = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, Deg(rot.Roll));
        return System.Numerics.Quaternion.Normalize(yaw * pitch * roll);
    }

    private static StationModule Pier() => new()
    {
        Id = "pier_arg",
        Name = "Harbor Pier",
        Macro = "pier_arg_harbor_01_macro",
        Kind = ModuleKind.Pier,
        Size = ModuleSize.L,
        Geometry = new ModuleGeometry
        {
            HalfExtents = new Vec3(200, 200, 200),
            Connections = [Snap("ConnectionSnap001", new Vec3(0, 0, -200), new Vec3(0, 0, -1))],
        },
    };

    [Fact]
    public void Layout_PlacesPiersOnHorizontalConnectorFaces()
    {
        var bodies = Enumerable.Range(0, 6)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var layout = new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 2)],
            Connectors = [Cross()],
        };

        var result = new StationLayoutEngine().Layout(layout);

        var piers = result.Modules.Where(m => m.Module.Kind == ModuleKind.Pier).ToList();
        Assert.Equal(2, piers.Count);

        // Cross() snaps 005/006 are the down/up (vertical) faces; piers must use a horizontal face.
        string[] verticalFaces = ["ConnectionSnap005", "ConnectionSnap006"];
        foreach (var pier in piers)
        {
            Assert.DoesNotContain(pier.PredecessorConnection, verticalFaces, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Layout_MountsDocksBeyondBodyModuleCentres()
    {
        // Production modules have a large footprint (half-extents 1200×600×600), so docks mounted on
        // the compact connector shell would be buried. The standoff must push them past the bodies.
        var bodies = Enumerable.Range(0, 10)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var layout = new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 3)],
            Connectors = [Cross()],
        };

        var result = new StationLayoutEngine().Layout(layout);
        var bodyModules = result.Modules.Where(m => m.Module.Kind == ModuleKind.Production).ToList();

        foreach (var pier in result.Modules.Where(m => m.Module.Kind == ModuleKind.Pier))
        {
            var axis = DominantAxis(pier.Position);
            var pierProj = Dot(pier.Position, axis);
            var maxBodyProj = bodyModules.Max(b => Dot(b.Position, axis));

            Assert.True(pierProj > maxBodyProj,
                $"pier not beyond body centres on its axis: {pierProj} <= {maxBodyProj}");
        }
    }

    [Fact]
    public void Layout_StandoffSpacersScaleWithBodySize()
    {
        StationLayout Station(Func<string, StationModule> body) => new()
        {
            Modules = Enumerable.Range(0, 6).Select(i => new LayoutItem(body("b" + i), 1)).ToList(),
            Docks = [new LayoutItem(Pier(), 2)],
            Connectors = [Cross()],
        };

        var small = new StationLayoutEngine().Layout(Station(SmallBody));
        var large = new StationLayoutEngine().Layout(Station(Production));

        var smallConnectors = small.Modules.Count(m => m.Module.Kind == ModuleKind.Connection);
        var largeConnectors = large.Modules.Count(m => m.Module.Kind == ModuleKind.Connection);

        // Large bodies protrude further, so more spacer connectors are inserted to clear them.
        Assert.True(largeConnectors > smallConnectors,
            $"expected more spacers for large bodies: {largeConnectors} <= {smallConnectors}");
    }

    [Fact]
    public void Layout_WithStandoff_RemainsValidSpanningTree()
    {
        var bodies = Enumerable.Range(0, 8)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var layout = new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 3)],
            Connectors = [Cross()],
        };

        var result = new StationLayoutEngine().Layout(layout);

        // Indices are 1..N and unique (spacer connectors included).
        Assert.Equal(
            Enumerable.Range(1, result.Modules.Count).ToHashSet(),
            result.Modules.Select(m => m.Index).ToHashSet());

        // Exactly one root; every other module snaps to an earlier entry with both snaps set.
        Assert.Single(result.Modules, m => m.PredecessorIndex is null);
        foreach (var m in result.Modules.Where(m => m.PredecessorIndex is not null))
        {
            Assert.True(m.PredecessorIndex < m.Index);
            Assert.True(m.PredecessorIndex >= 1);
            Assert.NotNull(m.Connection);
            Assert.NotNull(m.PredecessorConnection);
        }
    }

    [Fact]
    public void Layout_DistributesFourPiers_OnePerHorizontalDirection()
    {
        var bodies = Enumerable.Range(0, 6)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var result = new StationLayoutEngine().Layout(new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 4)],
            Connectors = [Cross()],
        });

        var dirs = result.Modules
            .Where(m => m.Module.Kind == ModuleKind.Pier)
            .Select(p => DominantAxis(p.Position))
            .ToList();

        Assert.Equal(4, dirs.Count);
        Assert.All(dirs, d => Assert.Equal(0.0, d.Y));      // all horizontal (no up/down)
        Assert.Equal(4, dirs.Distinct().Count());           // one in each of ±X / ±Z
    }

    [Fact]
    public void Layout_BalancesEightPiers_TwoPerHorizontalDirection()
    {
        var bodies = Enumerable.Range(0, 8)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var result = new StationLayoutEngine().Layout(new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 8)],
            Connectors = [Cross()],
        });

        var piers = result.Modules.Where(m => m.Module.Kind == ModuleKind.Pier).ToList();
        Assert.Equal(8, piers.Count);

        var groups = piers.GroupBy(p => DominantAxis(p.Position)).ToList();
        Assert.Equal(4, groups.Count);                              // all four directions used
        Assert.All(groups, g => Assert.Equal(0.0, g.Key.Y));        // all horizontal
        Assert.All(groups, g => Assert.Equal(2, g.Count()));        // balanced two-per-direction
    }

    [Fact]
    public void Layout_DistributesSixPiers_AsEvenlyAsPossible()
    {
        var bodies = Enumerable.Range(0, 6)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var result = new StationLayoutEngine().Layout(new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Pier(), 6)],
            Connectors = [Cross()],
        });

        var piers = result.Modules.Where(m => m.Module.Kind == ModuleKind.Pier).ToList();
        Assert.Equal(6, piers.Count);
        Assert.All(piers, p => Assert.Equal(0.0, DominantAxis(p.Position).Y)); // all horizontal

        var counts = piers.GroupBy(p => DominantAxis(p.Position)).Select(g => g.Count()).ToList();
        Assert.Equal(4, counts.Count);                          // every direction used
        Assert.True(counts.Max() - counts.Min() <= 1,           // 2,2,1,1 — never lopsided
            $"piers unbalanced across directions: {string.Join(",", counts)}");
    }

    [Fact]
    public void Layout_PlacesDocksOnTopYLayer_FacingUp()
    {
        var bodies = Enumerable.Range(0, 8)
            .Select(i => new LayoutItem(Production("prod_" + i), 1))
            .ToList();

        var result = new StationLayoutEngine().Layout(new StationLayout
        {
            Modules = bodies,
            Docks = [new LayoutItem(Dock(), 4)],
            Connectors = [Cross()],
        });

        var docks = result.Modules.Where(m => m.Module.Kind == ModuleKind.Dock).ToList();
        Assert.Equal(4, docks.Count);

        // Docks mount on horizontal faces (never ±Y) so their pads stay level...
        Assert.All(docks, d => Assert.NotEqual(new Vec3(0, 1, 0), DominantAxis(d.Position)));
        Assert.All(docks, d => Assert.NotEqual(new Vec3(0, -1, 0), DominantAxis(d.Position)));

        // ...and sit in the upper half of the station so their +Y docking pads face open space.
        Assert.All(docks, d => Assert.True(d.Position.Y >= 0,
            $"dock not in upper half: y={d.Position.Y}"));

        // Pure-yaw orientation keeps the dock's +Y pad pointing up (world +Y).
        foreach (var dock in docks)
        {
            var padWorld = WorldPoint(dock, new Vec3(0, 1, 0)) - dock.Position;
            Assert.True(padWorld.Y > 0.9, $"dock pad not facing up: {padWorld}");
        }
    }

    // A compact body whose footprint sits inside the connector shell (no standoff needed).
    private static StationModule SmallBody(string id) => new()
    {
        Id = id,
        Name = id,
        Macro = id + "_macro",
        Kind = ModuleKind.Production,
        Size = ModuleSize.S,
        Geometry = new ModuleGeometry
        {
            HalfExtents = new Vec3(150, 150, 150),
            Connections =
            [
                Snap("ConnectionSnap001", new Vec3(150, 0, 0), new Vec3(1, 0, 0)),
                Snap("ConnectionSnap002", new Vec3(-150, 0, 0), new Vec3(-1, 0, 0)),
            ],
        },
    };

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vec3 DominantAxis(Vec3 v)
    {
        var ax = Math.Abs(v.X);
        var ay = Math.Abs(v.Y);
        var az = Math.Abs(v.Z);
        if (ax >= ay && ax >= az) return new Vec3(Math.Sign(v.X), 0, 0);
        if (ay >= az) return new Vec3(0, Math.Sign(v.Y), 0);
        return new Vec3(0, 0, Math.Sign(v.Z));
    }
}
