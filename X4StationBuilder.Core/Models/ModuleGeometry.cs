namespace X4StationBuilder.Core.Models;

/// <summary>A 3D vector in blueprint centimetres (X4's native unit for module positions).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>
/// Euler rotation in degrees, matching the X4 blueprint <c>&lt;rotation yaw/pitch/roll&gt;</c> element.
/// </summary>
public readonly record struct Rotation(double Yaw = 0, double Pitch = 0, double Roll = 0)
{
    public static readonly Rotation None = new(0, 0, 0);

    /// <summary>True when all three angles are (near) zero, so no <c>&lt;rotation&gt;</c> need be emitted.</summary>
    public bool IsIdentity =>
        Math.Abs(Yaw) < 1e-3 && Math.Abs(Pitch) < 1e-3 && Math.Abs(Roll) < 1e-3;
}

/// <summary>Broad role of a module connection point, inferred from its <c>tags</c>.</summary>
public enum ConnectionKind
{
    /// <summary>A structural snap point (<c>tags</c> contains <c>snap</c>) used to link modules.</summary>
    Snap,

    /// <summary>A ship docking bay/berth (<c>tags</c> contains <c>dockingbay</c>).</summary>
    DockingBay,

    /// <summary>A docking approach exclusion volume (<c>tags</c> contains <c>exclusionzone</c>).</summary>
    ExclusionZone,

    /// <summary>Any other connection (parts, turrets, info, etc.) — not used by layout.</summary>
    Other,
}

/// <summary>
/// A single connection point on a module, parsed from the module's component XML
/// (<c>&lt;connection name tags&gt;&lt;offset&gt;&lt;position/&gt;&lt;quaternion/&gt;</c>).
/// Positions are in centimetres, relative to the module's origin.
/// </summary>
public sealed class ConnectionPoint
{
    /// <summary>Connection name (e.g. <c>ConnectionSnap001</c>), used as the blueprint connection ref.</summary>
    public required string Name { get; init; }

    /// <summary>Role inferred from <see cref="Tags"/>.</summary>
    public ConnectionKind Kind { get; init; } = ConnectionKind.Other;

    /// <summary>Raw whitespace-separated tags from the XML (e.g. <c>snap snap_aligned</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Local position offset (cm) from the module origin.</summary>
    public Vec3 Position { get; init; }

    /// <summary>
    /// Outward unit direction of this connection in module-local space, derived from its position
    /// (and orientation when available). For snap points this is the face the connection sits on.
    /// </summary>
    public Vec3 Direction { get; init; }
}

/// <summary>
/// Geometry for a station module parsed from its macro/component XML: the connection points used to
/// link modules, the derived footprint bounding box, and any docking exclusion zones.
/// </summary>
/// <remarks>
/// All distances are in blueprint centimetres. When geometry cannot be resolved the layout engine
/// falls back to a placeholder footprint derived from <see cref="StationModule.Size"/>.
/// </remarks>
public sealed class ModuleGeometry
{
    /// <summary>All parsed connection points (snaps, docking bays, exclusion zones, …).</summary>
    public IReadOnlyList<ConnectionPoint> Connections { get; init; } = [];

    /// <summary>Axis-aligned footprint half-extents (cm) from the module origin (so full size = 2×).</summary>
    public Vec3 HalfExtents { get; init; }

    /// <summary>Snap points usable for linking modules.</summary>
    public IEnumerable<ConnectionPoint> Snaps =>
        Connections.Where(c => c.Kind == ConnectionKind.Snap);

    /// <summary>Docking exclusion-zone connections (the dock "no-build" approach volumes).</summary>
    public IEnumerable<ConnectionPoint> ExclusionZones =>
        Connections.Where(c => c.Kind == ConnectionKind.ExclusionZone);

    /// <summary>True when the module exposes at least one structural snap point.</summary>
    public bool HasSnaps => Connections.Any(c => c.Kind == ConnectionKind.Snap);

    /// <summary>Full footprint size (cm) along each axis.</summary>
    public Vec3 Size => new(HalfExtents.X * 2, HalfExtents.Y * 2, HalfExtents.Z * 2);
}
