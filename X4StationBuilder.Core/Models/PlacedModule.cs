namespace X4StationBuilder.Core.Models;

/// <summary>
/// A module placed by the <see cref="Services.StationLayoutEngine"/>: its absolute blueprint
/// position and rotation, plus the snap-connection link back to its predecessor. This is the unit
/// the XML exporter (Step 10) turns into a <c>&lt;entry&gt;</c>.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is the module origin in blueprint centimetres, with (0,0,0) at the station
/// centre. When <see cref="PredecessorIndex"/> is set the module snaps to that earlier entry:
/// <see cref="Connection"/> is this module's snap and <see cref="PredecessorConnection"/> the
/// predecessor's snap. Root modules (the first entry) have no predecessor.
/// </remarks>
public sealed class PlacedModule
{
    /// <summary>1-based blueprint entry index.</summary>
    public required int Index { get; init; }

    /// <summary>The module being placed.</summary>
    public required StationModule Module { get; init; }

    /// <summary>Absolute position (cm) of the module origin, centred on the station at (0,0,0).</summary>
    public Vec3 Position { get; init; }

    /// <summary>Module rotation (degrees).</summary>
    public Rotation Rotation { get; init; }

    /// <summary>Index of the predecessor entry this module snaps to, or null for the root.</summary>
    public int? PredecessorIndex { get; init; }

    /// <summary>The predecessor's snap connection name this module attaches to.</summary>
    public string? PredecessorConnection { get; init; }

    /// <summary>This module's own snap connection name used for the attachment.</summary>
    public string? Connection { get; init; }

    /// <summary>Convenience: the blueprint macro for this module (may be null for un-resolved seeds).</summary>
    public string? Macro => Module.Macro;
}

/// <summary>
/// Input to the layout engine: a single module type and how many copies to place.
/// </summary>
public sealed record LayoutItem(StationModule Module, int Count);

/// <summary>
/// The full set of modules to lay out: production/storage/habitat bodies plus docks, and the
/// faction whose structural connector modules should frame the station.
/// </summary>
public sealed class StationLayout
{
    /// <summary>Functional modules (production, storage, habitat) and their counts.</summary>
    public IReadOnlyList<LayoutItem> Modules { get; init; } = [];

    /// <summary>Dock/pier modules and their counts; placed on the outer shell facing outward.</summary>
    public IReadOnlyList<LayoutItem> Docks { get; init; } = [];

    /// <summary>
    /// Structural connector modules available to frame the station (e.g. <c>struct_arg_cross_01</c>).
    /// The engine picks a cross-style connector from this list; if empty it synthesizes a generic
    /// 6-way connector so layout still works on un-scanned/bundled data.
    /// </summary>
    public IReadOnlyList<StationModule> Connectors { get; init; } = [];

    /// <summary>
    /// Station species/faction (e.g. "Terran"). When set, the engine prefers a structural connector
    /// of this faction (e.g. <c>struct_ter_cross_01</c>) so the framing matches the station's species,
    /// falling back to any suitable connector when the faction has none.
    /// </summary>
    public string? PreferredFaction { get; init; }
}

/// <summary>The result of laying out a station: positioned modules and the overall bounding box.</summary>
public sealed class LayoutResult
{
    /// <summary>All placed modules (bodies, inserted connectors, and docks) in entry order.</summary>
    public required IReadOnlyList<PlacedModule> Modules { get; init; }

    /// <summary>Axis-aligned bounding-box size (cm) of the whole station.</summary>
    public Vec3 BoundingBoxSize { get; init; }
}
