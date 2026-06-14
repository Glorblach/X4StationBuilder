using System.Numerics;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Assigns every module in a <see cref="StationLayout"/> a 3D blueprint position and rotation,
/// packing the station into a compact, near-cube shape centred at (0,0,0) with docks on the outer
/// shell facing outward.
/// </summary>
/// <remarks>
/// <para>
/// X4 modules link via <em>snap points</em> (see <see cref="ModuleGeometry"/>): a six-way structural
/// connector (<c>struct_*_cross</c>) exposes a snap on every axis face, whereas functional modules
/// usually expose only two (on a single axis). The engine therefore frames the station with a cube
/// grid of connectors (mated face-to-face) and hangs each functional module / dock off a free
/// connector snap, orienting it so one of its own snaps mates with the connector.
/// </para>
/// <para>
/// Output is game-authentic: every non-root entry records a <c>predecessor</c>/<c>connection</c>
/// snap pair <em>and</em> an absolute <c>offset</c> position, matching the real construction-plan
/// style. Where a module lacks parsed geometry the engine falls back to a per-<see cref="ModuleSize"/>
/// placeholder footprint and a synthesized six-way connector so layout still succeeds on bundled
/// (un-scanned) data.
/// </para>
/// </remarks>
public sealed class StationLayoutEngine
{
    /// <summary>Half the distance between two mated connector snaps (connectors snap at ±200 cm).</summary>
    private const double ConnectorSnapDistance = 200;

    /// <summary>Centre-to-centre distance between two mated connectors.</summary>
    private const double ConnectorSpacing = ConnectorSnapDistance * 2;

    private static readonly (int dx, int dy, int dz, Vec3 dir)[] AxisDirs =
    [
        (1, 0, 0, new Vec3(1, 0, 0)),
        (-1, 0, 0, new Vec3(-1, 0, 0)),
        (0, 1, 0, new Vec3(0, 1, 0)),
        (0, -1, 0, new Vec3(0, -1, 0)),
        (0, 0, 1, new Vec3(0, 0, 1)),
        (0, 0, -1, new Vec3(0, 0, -1)),
    ];

    /// <summary>Lays out the given station, returning positioned modules ready for XML export.</summary>
    public LayoutResult Layout(StationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var bodies = Flatten(layout.Modules);
        var docks = Flatten(layout.Docks);

        var connector = ChooseConnector(layout.Connectors);
        var connectorGeometry = connector.Geometry ?? SyntheticConnectorGeometry();

        var grid = BuildConnectorGrid(bodies.Count + docks.Count);
        var freeSnaps = CollectFreeSnaps(grid, connectorGeometry);

        // Docks take the outermost free snaps (so their approach corridors face open space); bodies
        // fill the rest, preferring inner snaps to keep the cube compact. Piers fan out symmetrically
        // across the four horizontal faces (±X/±Z), stacking extras on distinct Y-layers; S/M docks
        // prefer the top (+Y) face. See <see cref="SelectDockSnaps"/>.
        var pool = freeSnaps
            .OrderByDescending(s => s.OutwardDistance)
            .ToList();

        var dockPlacements = SelectDockSnaps(docks, pool, grid);

        var bodySnaps = pool;

        var placed = new List<PlacedModule>();
        var index = 1;

        // 1. Connector skeleton (root first, then each connector snapped to its grid parent).
        var connectorIndexByCell = new Dictionary<(int, int, int), int>();
        foreach (var node in grid.NodesInBfsOrder)
        {
            int? predIndex = null;
            string? predConn = null;
            string? ownConn = null;

            if (node.Parent is { } parent)
            {
                var dir = DirOf(parent, node.Cell);
                predIndex = connectorIndexByCell[parent];
                predConn = SnapNameForDirection(connectorGeometry, dir);
                ownConn = SnapNameForDirection(connectorGeometry, Negate(dir));
            }

            placed.Add(new PlacedModule
            {
                Index = index,
                Module = connector,
                Position = CellToWorld(node.Cell, grid),
                Rotation = Rotation.None,
                PredecessorIndex = predIndex,
                PredecessorConnection = predConn,
                Connection = ownConn,
            });
            connectorIndexByCell[node.Cell] = index;
            index++;
        }

        // 2. Attach functional bodies, then docks, to free connector snaps.
        var bodyPlacements = new List<PlacedModule>();
        foreach (var (module, snap) in Zip(bodies, bodySnaps))
        {
            var p = AttachModule(module, snap, connectorGeometry, connectorIndexByCell, grid, index);
            placed.Add(p);
            bodyPlacements.Add(p);
            index++;
        }

        // Docks/piers mount on the outer connector shell, which sits deep inside the body envelope
        // (long production modules reach far past the compact connector cube). Bridge each dock out
        // past the body with a short chain of spacer connectors so its bays/corridors face open space.
        foreach (var (module, snap) in dockPlacements)
        {
            var reach = BodyReach(bodyPlacements, snap.Outward);
            var added = AttachModuleWithStandoff(
                module, snap, connector, connectorGeometry, connectorIndexByCell, grid, reach, ref index);
            placed.AddRange(added);
        }

        Recentre(placed);

        return new LayoutResult
        {
            Modules = placed,
            BoundingBoxSize = BoundingBox(placed),
        };
    }

    /// <summary>Attaches a single hung module to a free connector snap, mating their snap points.</summary>
    private static PlacedModule AttachModule(
        StationModule module,
        FreeSnap snap,
        ModuleGeometry connectorGeometry,
        Dictionary<(int, int, int), int> connectorIndexByCell,
        ConnectorGrid grid,
        int index)
    {
        var geometry = module.Geometry ?? PlaceholderGeometry(module);
        var moduleSnap = ChooseModuleSnap(geometry);

        // Orient the module so its chosen snap faces back toward the connector (-outward). For
        // horizontal mounts this is a pure yaw, which keeps a dock's +Y docking pad facing up.
        var rotation = RotationMapping(moduleSnap.Direction, Negate(snap.Outward));
        var quat = QuatFromEuler(rotation);

        // Mate point: where the connector's snap sits in world space.
        var connectorWorld = CellToWorld(snap.Cell, grid);
        var matePoint = connectorWorld + snap.Outward * ConnectorSnapDistance;
        var origin = matePoint - Rotate(quat, moduleSnap.Position);

        return new PlacedModule
        {
            Index = index,
            Module = module,
            Position = origin,
            Rotation = rotation,
            PredecessorIndex = connectorIndexByCell[snap.Cell],
            PredecessorConnection = SnapNameForDirection(connectorGeometry, snap.Outward),
            Connection = moduleSnap.Name,
        };
    }

    /// <summary>Extra clearance (cm) added beyond the body envelope before a dock is mounted.</summary>
    private const double DockStandoffClearance = 600;

    /// <summary>
    /// Mounts a dock/pier, first bridging it outward past the body envelope with a chain of spacer
    /// connectors so its docking bays and approach corridor face open space. When the body does not
    /// protrude past the connector shell on this face, no spacers are inserted and the dock mounts
    /// directly. Returns every module produced (spacers followed by the dock), assigning sequential
    /// indices from <paramref name="index"/> (advanced past the produced modules).
    /// </summary>
    private static List<PlacedModule> AttachModuleWithStandoff(
        StationModule module,
        FreeSnap snap,
        StationModule connector,
        ModuleGeometry connectorGeometry,
        Dictionary<(int, int, int), int> connectorIndexByCell,
        ConnectorGrid grid,
        double bodyReach,
        ref int index)
    {
        var result = new List<PlacedModule>();

        var shellCentre = CellToWorld(snap.Cell, grid);
        var shellFaceDist = Dot(shellCentre, snap.Outward) + ConnectorSnapDistance;
        var targetDist = bodyReach + DockStandoffClearance;

        var spacerCount = (int)Math.Ceiling((targetDist - shellFaceDist) / ConnectorSpacing);
        if (spacerCount < 0)
        {
            spacerCount = 0;
        }

        // Snap names for mating connectors face-to-face along the outward axis.
        var outwardSnap = SnapNameForDirection(connectorGeometry, snap.Outward);
        var inwardSnap = SnapNameForDirection(connectorGeometry, Negate(snap.Outward));

        var predIndex = connectorIndexByCell[snap.Cell];
        var predConn = outwardSnap;

        // Chain of spacer connectors stepping outward by one connector spacing each.
        for (var i = 1; i <= spacerCount; i++)
        {
            var centre = shellCentre + snap.Outward * (ConnectorSpacing * i);
            result.Add(new PlacedModule
            {
                Index = index,
                Module = connector,
                Position = centre,
                Rotation = Rotation.None,
                PredecessorIndex = predIndex,
                PredecessorConnection = predConn,
                Connection = inwardSnap,
            });

            predIndex = index;
            predConn = outwardSnap;
            index++;
        }

        // Mount the dock/pier on the outermost connector's outward face.
        var geometry = module.Geometry ?? PlaceholderGeometry(module);
        var moduleSnap = ChooseModuleSnap(geometry);
        var rotation = RotationMapping(moduleSnap.Direction, Negate(snap.Outward));
        var quat = QuatFromEuler(rotation);

        var mountCentre = shellCentre + snap.Outward * (ConnectorSpacing * spacerCount);
        var matePoint = mountCentre + snap.Outward * ConnectorSnapDistance;
        var origin = matePoint - Rotate(quat, moduleSnap.Position);

        result.Add(new PlacedModule
        {
            Index = index,
            Module = module,
            Position = origin,
            Rotation = rotation,
            PredecessorIndex = predIndex,
            PredecessorConnection = predConn,
            Connection = moduleSnap.Name,
        });
        index++;

        return result;
    }

    /// <summary>
    /// The station body's outward reach along axis direction <paramref name="dir"/>: the farthest any
    /// body module extends, using a conservative bounding-sphere radius so rotated footprints are
    /// fully covered.
    /// </summary>
    private static double BodyReach(IReadOnlyList<PlacedModule> bodies, Vec3 dir)
    {
        var reach = 0.0;
        foreach (var b in bodies)
        {
            var geometry = b.Module.Geometry ?? PlaceholderGeometry(b.Module);
            var radius = geometry.HalfExtents.Length;
            reach = Math.Max(reach, Dot(b.Position, dir) + radius);
        }

        return reach;
    }

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // ----- Connector grid -------------------------------------------------------------------

    private sealed record GridNode((int, int, int) Cell, (int, int, int)? Parent);

    private sealed class ConnectorGrid
    {
        public required int Nx { get; init; }
        public required int Ny { get; init; }
        public required int Nz { get; init; }
        public required IReadOnlyList<GridNode> NodesInBfsOrder { get; init; }
        public required HashSet<(int, int, int)> Cells { get; init; }
    }

    /// <summary>
    /// Builds the smallest near-cube grid of connectors whose free snap count covers
    /// <paramref name="slotsNeeded"/> hung modules.
    /// </summary>
    private static ConnectorGrid BuildConnectorGrid(int slotsNeeded)
    {
        var dims = new[] { 1, 1, 1 };
        while (FreeSnapCount(dims[0], dims[1], dims[2]) < Math.Max(1, slotsNeeded))
        {
            // Grow the smallest dimension to keep the grid as cube-like as possible.
            var minIdx = 0;
            if (dims[1] < dims[minIdx]) minIdx = 1;
            if (dims[2] < dims[minIdx]) minIdx = 2;
            dims[minIdx]++;
        }

        var (nx, ny, nz) = (dims[0], dims[1], dims[2]);
        var cells = new HashSet<(int, int, int)>();
        for (var x = 0; x < nx; x++)
        for (var y = 0; y < ny; y++)
        for (var z = 0; z < nz; z++)
        {
            cells.Add((x, y, z));
        }

        return new ConnectorGrid
        {
            Nx = nx,
            Ny = ny,
            Nz = nz,
            Cells = cells,
            NodesInBfsOrder = BfsSpanningTree(cells, nx, ny, nz),
        };
    }

    /// <summary>BFS spanning tree from the centre cell, recording each node's parent.</summary>
    private static List<GridNode> BfsSpanningTree(
        HashSet<(int, int, int)> cells, int nx, int ny, int nz)
    {
        var root = ((nx - 1) / 2, (ny - 1) / 2, (nz - 1) / 2);
        var order = new List<GridNode> { new(root, null) };
        var visited = new HashSet<(int, int, int)> { root };
        var queue = new Queue<(int, int, int)>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (dx, dy, dz, _) in AxisDirs)
            {
                var next = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                if (cells.Contains(next) && visited.Add(next))
                {
                    order.Add(new GridNode(next, cell));
                    queue.Enqueue(next);
                }
            }
        }

        return order;
    }

    private static int FreeSnapCount(int nx, int ny, int nz)
    {
        var crosses = nx * ny * nz;
        var adjacencies =
            (nx - 1) * ny * nz +
            nx * (ny - 1) * nz +
            nx * ny * (nz - 1);
        return 6 * crosses - 2 * adjacencies;
    }

    private sealed record FreeSnap((int, int, int) Cell, Vec3 Outward, double OutwardDistance);

    /// <summary>Every connector face that is not mated to a neighbouring connector.</summary>
    private static List<FreeSnap> CollectFreeSnaps(ConnectorGrid grid, ModuleGeometry connectorGeometry)
    {
        var centre = new Vec3((grid.Nx - 1) / 2.0, (grid.Ny - 1) / 2.0, (grid.Nz - 1) / 2.0);
        var result = new List<FreeSnap>();

        foreach (var cell in grid.Cells)
        {
            foreach (var (dx, dy, dz, dir) in AxisDirs)
            {
                var neighbour = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                if (grid.Cells.Contains(neighbour))
                {
                    continue; // mated to a neighbouring connector
                }

                if (SnapNameForDirection(connectorGeometry, dir) is null)
                {
                    continue; // connector has no snap on this face
                }

                var cellVec = new Vec3(cell.Item1, cell.Item2, cell.Item3);
                var outwardDist = (cellVec - centre + dir * 0.5).Length;
                result.Add(new FreeSnap(cell, dir, outwardDist));
            }
        }

        return result;
    }

    /// <summary>The four horizontal outward directions piers fan out across, in round-robin order.</summary>
    private static readonly Vec3[] HorizontalDirs =
    [
        new Vec3(1, 0, 0),
        new Vec3(-1, 0, 0),
        new Vec3(0, 0, 1),
        new Vec3(0, 0, -1),
    ];

    /// <summary>
    /// Assigns each dock/pier to a free connector snap and removes the used snaps from
    /// <paramref name="pool"/> (leaving the remainder for bodies). Both piers and docks mount only on
    /// the <em>horizontal</em> faces (±X/±Z) — never ±Y — and are distributed <em>symmetrically</em>
    /// round-robin across those four directions so they fan out instead of clustering. Piers take the
    /// outermost, most face-centred snap first, stacking extras across Y-layers from the centre
    /// outward. S/M docks take the outermost snap on the <em>top</em> Y-layer first, so their
    /// upward-facing docking pads look out into open space above the station body.
    /// </summary>
    private static List<(StationModule Module, FreeSnap Snap)> SelectDockSnaps(
        List<StationModule> docks, List<FreeSnap> pool, ConnectorGrid grid)
    {
        var result = new List<(StationModule, FreeSnap)>();

        var piers = docks.Where(d => d.Kind == ModuleKind.Pier).ToList();
        var others = docks.Where(d => d.Kind != ModuleKind.Pier).ToList();

        // Piers: outermost, laterally centred, then spread across Y-layers from the centre out.
        AssignHorizontalRoundRobin(piers, pool, grid, result, dir => s =>
            (-OutwardProjection(s, grid, dir), LateralOffset(s, grid, dir), LayerOrder(s, grid)));

        // Docks: outermost, on the top Y-layer first (pad faces open space), then laterally centred.
        AssignHorizontalRoundRobin(others, pool, grid, result, dir => s =>
            (-OutwardProjection(s, grid, dir), -CellY(s, grid), LateralOffset(s, grid, dir)));

        return result;
    }

    /// <summary>
    /// Distributes <paramref name="modules"/> round-robin across the four horizontal faces, picking
    /// each face's next snap by the per-face ordering <paramref name="keyFor"/> (smaller key first).
    /// Chosen snaps are removed from <paramref name="pool"/> and appended to <paramref name="result"/>.
    /// </summary>
    private static void AssignHorizontalRoundRobin(
        List<StationModule> modules,
        List<FreeSnap> pool,
        ConnectorGrid grid,
        List<(StationModule, FreeSnap)> result,
        Func<Vec3, Func<FreeSnap, (double, double, double)>> keyFor)
    {
        if (modules.Count == 0)
        {
            return;
        }

        var perDir = new Dictionary<(double, double), Queue<FreeSnap>>();
        foreach (var dir in HorizontalDirs)
        {
            var orderedForDir = pool
                .Where(s => SameDir(s.Outward, dir))
                .OrderBy(keyFor(dir))
                .ToList();
            perDir[(dir.X, dir.Z)] = new Queue<FreeSnap>(orderedForDir);
        }

        var cursor = 0;
        foreach (var module in modules)
        {
            FreeSnap? chosen = null;
            for (var k = 0; k < HorizontalDirs.Length; k++)
            {
                var dir = HorizontalDirs[(cursor + k) % HorizontalDirs.Length];
                var queue = perDir[(dir.X, dir.Z)];
                if (queue.Count > 0)
                {
                    chosen = queue.Dequeue();
                    cursor = (cursor + k + 1) % HorizontalDirs.Length;
                    break;
                }
            }

            // No horizontal snap left: fall back to any remaining snap so the module is still placed.
            chosen ??= pool.FirstOrDefault(IsHorizontal) ?? pool.FirstOrDefault();
            if (chosen is not null)
            {
                pool.Remove(chosen);
                result.Add((module, chosen));
            }
        }
    }

    private static Vec3 GridCentre(ConnectorGrid grid) =>
        new((grid.Nx - 1) / 2.0, (grid.Ny - 1) / 2.0, (grid.Nz - 1) / 2.0);

    /// <summary>How far a snap's cell sits along <paramref name="dir"/> from the grid centre.</summary>
    private static double OutwardProjection(FreeSnap s, ConnectorGrid grid, Vec3 dir) =>
        Dot(new Vec3(s.Cell.Item1, s.Cell.Item2, s.Cell.Item3) - GridCentre(grid), dir);

    /// <summary>Distance of a snap from the face centre along the horizontal axis perpendicular to
    /// <paramref name="dir"/> (used to prefer face-centred piers/docks).</summary>
    private static double LateralOffset(FreeSnap s, ConnectorGrid grid, Vec3 dir)
    {
        var rel = new Vec3(s.Cell.Item1, s.Cell.Item2, s.Cell.Item3) - GridCentre(grid);
        var perp = new Vec3(dir.Z, 0, dir.X); // the other horizontal axis
        return Math.Abs(Dot(rel, perp));
    }

    /// <summary>Stacking order across Y-layers: centre layer first, then up, then down, spreading out.</summary>
    private static double LayerOrder(FreeSnap s, ConnectorGrid grid)
    {
        var y = s.Cell.Item2 - GridCentre(grid).Y;
        return Math.Abs(y) * 2 - (y > 0 ? 1 : 0);
    }

    /// <summary>Signed Y-layer of a snap's cell relative to the grid centre (higher = nearer the top).</summary>
    private static double CellY(FreeSnap s, ConnectorGrid grid) => s.Cell.Item2 - GridCentre(grid).Y;

    private static Vec3 CellToWorld((int, int, int) cell, ConnectorGrid grid) => new(
        (cell.Item1 - (grid.Nx - 1) / 2.0) * ConnectorSpacing,
        (cell.Item2 - (grid.Ny - 1) / 2.0) * ConnectorSpacing,
        (cell.Item3 - (grid.Nz - 1) / 2.0) * ConnectorSpacing);

    private static Vec3 DirOf((int, int, int) from, (int, int, int) to) => new(
        Math.Sign(to.Item1 - from.Item1),
        Math.Sign(to.Item2 - from.Item2),
        Math.Sign(to.Item3 - from.Item3));

    // ----- Connector / module selection ----------------------------------------------------

    /// <summary>Picks the best six-way connector from the supplied list (prefers a "cross").</summary>
    private static StationModule ChooseConnector(IReadOnlyList<StationModule> connectors)
    {
        var candidate = connectors
            .Where(c => c.Geometry is { } g && g.Snaps.Count() >= 6)
            .OrderByDescending(c => (c.Macro ?? c.Id).Contains("cross", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.Geometry!.Snaps.Count())
            .FirstOrDefault();

        return candidate ?? SyntheticConnector();
    }

    private static StationModule SyntheticConnector() => new()
    {
        Id = "struct_gen_cross",
        Name = "Structural Connector",
        Macro = "struct_gen_cross_01_macro",
        Kind = ModuleKind.Connection,
        Geometry = SyntheticConnectorGeometry(),
    };

    /// <summary>A generic six-way connector: a snap on every axis face at ±200 cm.</summary>
    private static ModuleGeometry SyntheticConnectorGeometry()
    {
        var conns = new List<ConnectionPoint>();
        for (var i = 0; i < AxisDirs.Length; i++)
        {
            var dir = AxisDirs[i].dir;
            conns.Add(new ConnectionPoint
            {
                Name = $"ConnectionSnap{i + 1:D3}",
                Kind = ConnectionKind.Snap,
                Tags = ["snap"],
                Position = dir * ConnectorSnapDistance,
                Direction = dir,
            });
        }

        return new ModuleGeometry
        {
            Connections = conns,
            HalfExtents = new Vec3(ConnectorSnapDistance, ConnectorSnapDistance, ConnectorSnapDistance),
        };
    }

    /// <summary>The connector snap name whose outward direction matches <paramref name="dir"/>.</summary>
    private static string? SnapNameForDirection(ModuleGeometry geometry, Vec3 dir) =>
        geometry.Snaps.FirstOrDefault(s => SameDir(s.Direction, dir))?.Name;

    /// <summary>Chooses a usable snap on a hung module (prefers one with a defined direction).</summary>
    private static ConnectionPoint ChooseModuleSnap(ModuleGeometry geometry)
    {
        var snap = geometry.Snaps.FirstOrDefault(s => s.Direction.Length > 0.5)
                   ?? geometry.Snaps.FirstOrDefault();

        // No snaps at all: synthesize one on +Z so the module can still be attached.
        return snap ?? new ConnectionPoint
        {
            Name = "ConnectionSnap001",
            Kind = ConnectionKind.Snap,
            Tags = ["snap"],
            Position = new Vec3(0, 0, ConnectorSnapDistance),
            Direction = new Vec3(0, 0, 1),
        };
    }

    /// <summary>Per-<see cref="ModuleSize"/> placeholder geometry for modules with no parsed data.</summary>
    private static ModuleGeometry PlaceholderGeometry(StationModule module)
    {
        var half = module.Size switch
        {
            ModuleSize.S => 300.0,
            ModuleSize.M => 600.0,
            ModuleSize.L => 900.0,
            ModuleSize.XL => 1200.0,
            _ => 300.0,
        };

        return new ModuleGeometry
        {
            HalfExtents = new Vec3(half, half, half),
            Connections =
            [
                new ConnectionPoint
                {
                    Name = "ConnectionSnap001",
                    Kind = ConnectionKind.Snap,
                    Tags = ["snap"],
                    Position = new Vec3(0, 0, half),
                    Direction = new Vec3(0, 0, 1),
                },
                new ConnectionPoint
                {
                    Name = "ConnectionSnap002",
                    Kind = ConnectionKind.Snap,
                    Tags = ["snap"],
                    Position = new Vec3(0, 0, -half),
                    Direction = new Vec3(0, 0, -1),
                },
            ],
        };
    }

    // ----- Geometry helpers ----------------------------------------------------------------

    private static void Recentre(List<PlacedModule> placed)
    {
        if (placed.Count == 0)
        {
            return;
        }

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in placed)
        {
            min = new Vec3(Math.Min(min.X, p.Position.X), Math.Min(min.Y, p.Position.Y), Math.Min(min.Z, p.Position.Z));
            max = new Vec3(Math.Max(max.X, p.Position.X), Math.Max(max.Y, p.Position.Y), Math.Max(max.Z, p.Position.Z));
        }

        var centre = (min + max) * 0.5;
        for (var i = 0; i < placed.Count; i++)
        {
            placed[i] = Shift(placed[i], centre);
        }
    }

    private static PlacedModule Shift(PlacedModule p, Vec3 centre) => new()
    {
        Index = p.Index,
        Module = p.Module,
        Position = p.Position - centre,
        Rotation = p.Rotation,
        PredecessorIndex = p.PredecessorIndex,
        PredecessorConnection = p.PredecessorConnection,
        Connection = p.Connection,
    };

    private static Vec3 BoundingBox(IReadOnlyList<PlacedModule> placed)
    {
        if (placed.Count == 0)
        {
            return Vec3.Zero;
        }

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in placed)
        {
            var g = p.Module.Geometry ?? PlaceholderGeometry(p.Module);
            var he = g.HalfExtents;
            min = new Vec3(Math.Min(min.X, p.Position.X - he.X), Math.Min(min.Y, p.Position.Y - he.Y), Math.Min(min.Z, p.Position.Z - he.Z));
            max = new Vec3(Math.Max(max.X, p.Position.X + he.X), Math.Max(max.Y, p.Position.Y + he.Y), Math.Max(max.Z, p.Position.Z + he.Z));
        }

        return max - min;
    }

    private static bool SameDir(Vec3 a, Vec3 b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Z - b.Z) < 1e-6;

    /// <summary>True when a free snap faces horizontally (outward in the XZ plane, no vertical tilt).</summary>
    private static bool IsHorizontal(FreeSnap snap) => Math.Abs(snap.Outward.Y) < 1e-6;

    private static Vec3 Negate(Vec3 v) => new(-v.X, -v.Y, -v.Z);

    private static IEnumerable<(StationModule, FreeSnap)> Zip(
        List<StationModule> modules, List<FreeSnap> snaps)
    {
        var n = Math.Min(modules.Count, snaps.Count);
        for (var i = 0; i < n; i++)
        {
            yield return (modules[i], snaps[i]);
        }
    }

    private static List<StationModule> Flatten(IReadOnlyList<LayoutItem> items)
    {
        var result = new List<StationModule>();
        foreach (var item in items)
        {
            for (var i = 0; i < item.Count; i++)
            {
                result.Add(item.Module);
            }
        }

        return result;
    }

    // ----- Rotation math (principal axes only) ---------------------------------------------

    private static readonly Vector3[] PrincipalAxes =
    [
        Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
    ];

    /// <summary>The rotation that maps unit direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    private static Rotation RotationMapping(Vec3 from, Vec3 to)
    {
        var f = ToNumerics(from);
        var t = ToNumerics(to);
        if (f.LengthSquared() < 1e-6 || t.LengthSquared() < 1e-6)
        {
            return Rotation.None;
        }

        f = Vector3.Normalize(f);
        t = Vector3.Normalize(t);

        Quaternion q;
        var dot = Vector3.Dot(f, t);
        if (dot > 0.9999f)
        {
            q = Quaternion.Identity;
        }
        else if (dot < -0.9999f)
        {
            // 180°: rotate about any axis perpendicular to f.
            var axis = PrincipalAxes.First(a => Math.Abs(Vector3.Dot(a, f)) < 0.5f);
            var perp = Vector3.Normalize(Vector3.Cross(f, axis));
            q = Quaternion.CreateFromAxisAngle(perp, MathF.PI);
        }
        else
        {
            var axis = Vector3.Normalize(Vector3.Cross(f, t));
            q = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(dot, -1f, 1f)));
        }

        return EulerFromQuaternion(q);
    }

    /// <summary>
    /// Decomposes a principal-axis quaternion into X4 yaw/pitch/roll by matching against the finite
    /// set of 90°-step euler triples (rotations here are always multiples of 90°).
    /// </summary>
    private static Rotation EulerFromQuaternion(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        var steps = new[] { 0.0, 90.0, 180.0, 270.0 };
        foreach (var yaw in steps)
        foreach (var pitch in steps)
        foreach (var roll in steps)
        {
            var candidate = QuatFromEuler(new Rotation(yaw, pitch, roll));
            if (Math.Abs(Quaternion.Dot(q, candidate)) > 0.9999f)
            {
                return new Rotation(Normalize180(yaw), Normalize180(pitch), Normalize180(roll));
            }
        }

        return Rotation.None;
    }

    /// <summary>Builds a quaternion from yaw(Y)/pitch(X)/roll(Z), applied roll → pitch → yaw.</summary>
    private static Quaternion QuatFromEuler(Rotation r)
    {
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Deg(r.Yaw));
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Deg(r.Pitch));
        var roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Deg(r.Roll));
        return Quaternion.Normalize(yaw * pitch * roll);
    }

    private static Vec3 Rotate(Quaternion q, Vec3 v)
    {
        var r = Vector3.Transform(ToNumerics(v), q);
        return new Vec3(r.X, r.Y, r.Z);
    }

    private static Vector3 ToNumerics(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static float Deg(double degrees) => (float)(degrees * Math.PI / 180.0);

    private static double Normalize180(double angle)
    {
        angle %= 360.0;
        if (angle > 180.0) angle -= 360.0;
        if (angle <= -180.0) angle += 360.0;
        return angle;
    }
}
