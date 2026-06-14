namespace X4StationBuilder.Core.Models;

/// <summary>
/// Broad category of a station module, derived from its macro/id prefix.
/// </summary>
public enum ModuleKind
{
    Unknown,
    Production,
    Storage,
    Dock,
    Pier,
    Habitat,
    Build,
    Defence,
    Connection,
    Equipment,
    Welfare,
    Processing,
    Recycling,
    Other,
}

/// <summary>Size class of a station module, parsed from its macro/id naming tokens.</summary>
public enum ModuleSize
{
    Unknown,
    S,
    M,
    L,
    XL,
}

/// <summary>Cargo type a storage module holds, parsed from its macro/id naming tokens.</summary>
public enum CargoType
{
    None,
    Container,
    Solid,
    Liquid,
}

/// <summary>
/// A station module available in the scanned X4 install.
/// </summary>
/// <remarks>
/// Assembled from <c>libraries/modules.xml</c> (logical module definition: produced ware, races,
/// factions) and <c>libraries/modulegroups.xml</c> (the concrete macro name placed in blueprints).
/// Geometry (footprint, connection points) lives in per-macro component XML and is deferred to a
/// later step, so those fields may be unset here.
/// </remarks>
public sealed class StationModule
{
    /// <summary>Logical module id from <c>modules.xml</c> (e.g. <c>prod_gen_energycells</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Localized module name (e.g. "Energy Cell Production"), if resolved; else null.</summary>
    public string? Name { get; init; }

    /// <summary>Concrete blueprint macro name (e.g. <c>prod_gen_energycells_macro</c>), if resolved.</summary>
    public string? Macro { get; init; }

    /// <summary>Module group id linking <c>modules.xml</c> to <c>modulegroups.xml</c>.</summary>
    public string? Group { get; init; }

    /// <summary>Broad category inferred from the id prefix.</summary>
    public ModuleKind Kind { get; init; } = ModuleKind.Unknown;

    /// <summary>Size class (S/M/L/XL) parsed from the macro/id naming tokens; Unknown if absent.</summary>
    public ModuleSize Size { get; init; } = ModuleSize.Unknown;

    /// <summary>Cargo type for storage modules; None for non-storage or unknown.</summary>
    public CargoType CargoType { get; init; } = CargoType.None;

    /// <summary>Primary ware produced by the module, if any (internal ware id).</summary>
    public string? ProducedWare { get; init; }

    /// <summary>Factions that can build/own this module.</summary>
    public IReadOnlyList<string> Factions { get; init; } = [];

    /// <summary>Races associated with the module's macro variants.</summary>
    public IReadOnlyList<string> Races { get; init; } = [];

    /// <summary>Workforce the module can employ (production) or house (habitat); 0 if unknown.</summary>
    public int WorkforceCapacity { get; init; }

    /// <summary>Cargo capacity in m³ for storage modules (from the macro's <c>&lt;cargo max&gt;</c>); 0 if unknown.</summary>
    public int CargoCapacity { get; init; }

    /// <summary>Id of the extension that supplied this module, or null for the base game.</summary>
    public string? SourceExtension { get; init; }

    /// <summary>
    /// Parsed footprint and connection-point geometry from the module's macro/component XML, used by
    /// the layout engine (Step 09). Null when geometry could not be resolved (e.g. bundled seed data),
    /// in which case the layout engine falls back to a per-<see cref="Size"/> placeholder footprint.
    /// </summary>
    public ModuleGeometry? Geometry { get; init; }

    /// <summary>Friendly label for the module: its <see cref="Name"/> when known, else its <see cref="Id"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
}
