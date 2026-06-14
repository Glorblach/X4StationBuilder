using System.Globalization;
using System.Xml.Linq;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Serializes a positioned <see cref="LayoutResult"/> (from <see cref="StationLayoutEngine"/>) into a
/// game-importable X4 construction-plan XML document (<c>&lt;plans&gt;/&lt;plan&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// Output mirrors the real plans the game writes: each <see cref="PlacedModule"/> becomes an
/// <c>&lt;entry&gt;</c> with a sequential 1-based index, its macro, an optional
/// <c>connection</c>/<c>&lt;predecessor&gt;</c> snap pair (only when the layout set one), and an
/// <c>&lt;offset&gt;</c> whose zero-valued position axes and all-zero rotation are omitted exactly the
/// way the game does.
/// </para>
/// <para>
/// A <c>&lt;patches&gt;</c> block is emitted only for the DLC extensions an actually-placed module came
/// from (<see cref="StationModule.SourceExtension"/>), joined to the scan's <see cref="DlcInfo"/> list
/// for version/name. Base-game-only layouts omit <c>&lt;patches&gt;</c> entirely.
/// </para>
/// <para>
/// The plan <c>id</c> uses the <c>player_{unixEpochSeconds}</c> form from the documented schema (real
/// in-game files use <c>{GUID}_{epoch}</c>; the game accepts either). Pass
/// <see cref="ExportOptions.PlanId"/> to override it for deterministic output/tests.
/// </para>
/// </remarks>
public sealed class BlueprintXmlExporter
{
    /// <summary>Below this absolute magnitude a position axis is treated as zero and omitted.</summary>
    private const double ZeroEpsilon = 1e-4;

    /// <summary>Inputs controlling a single export.</summary>
    public sealed class ExportOptions
    {
        /// <summary>Plan name shown in-game (the <c>plan/@name</c>).</summary>
        public required string PlanName { get; init; }

        /// <summary>Optional plan description (<c>plan/@description</c>); empty by default.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Detected DLCs (from the scan metadata) used to resolve version/name for the
        /// <c>&lt;patches&gt;</c> block. May be null/empty (e.g. bundled data) — patches are then
        /// emitted with only the extension id, or omitted if no placed module is DLC-sourced.
        /// </summary>
        public IReadOnlyList<DlcInfo>? Dlcs { get; init; }

        /// <summary>Explicit plan id; when null a <c>player_{unixEpochSeconds}</c> id is generated.</summary>
        public string? PlanId { get; init; }
    }

    /// <summary>Builds the construction-plan XML document for the given layout.</summary>
    public XDocument BuildDocument(LayoutResult layout, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);

        var plan = new XElement(
            "plan",
            new XAttribute("id", options.PlanId ?? GeneratePlanId()),
            new XAttribute("name", options.PlanName ?? string.Empty),
            new XAttribute("description", options.Description ?? string.Empty));

        var patches = BuildPatches(layout, options.Dlcs);
        if (patches is not null)
        {
            plan.Add(patches);
        }

        foreach (var module in layout.Modules)
        {
            plan.Add(BuildEntry(module));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("plans", plan));
    }

    /// <summary>Serializes the layout to an XML string (with declaration).</summary>
    public string ExportToString(LayoutResult layout, ExportOptions options)
    {
        var doc = BuildDocument(layout, options);
        using var writer = new Utf8StringWriter();
        doc.Save(writer);
        return writer.ToString();
    }

    /// <summary>Writes the layout to <paramref name="path"/> as UTF-8 construction-plan XML.</summary>
    public void ExportToFile(LayoutResult layout, ExportOptions options, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var doc = BuildDocument(layout, options);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        doc.Save(path);
    }

    private static XElement BuildEntry(PlacedModule module)
    {
        var entry = new XElement(
            "entry",
            new XAttribute("index", module.Index),
            new XAttribute("macro", module.Macro ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(module.Connection))
        {
            entry.Add(new XAttribute("connection", NormalizeConnection(module.Connection)));
        }

        if (module.PredecessorIndex is { } predIndex)
        {
            var predecessor = new XElement("predecessor", new XAttribute("index", predIndex));
            if (!string.IsNullOrWhiteSpace(module.PredecessorConnection))
            {
                predecessor.Add(new XAttribute("connection", NormalizeConnection(module.PredecessorConnection)));
            }

            entry.Add(predecessor);
        }

        entry.Add(BuildOffset(module));
        return entry;
    }

    private static XElement BuildOffset(PlacedModule module)
    {
        var position = new XElement("position");
        AddAxis(position, "x", module.Position.X);
        AddAxis(position, "y", module.Position.Y);
        AddAxis(position, "z", module.Position.Z);

        var offset = new XElement("offset", position);

        var rotation = BuildRotation(module.Rotation);
        if (rotation is not null)
        {
            offset.Add(rotation);
        }

        return offset;
    }

    private static void AddAxis(XElement position, string axis, double value)
    {
        if (Math.Abs(value) >= ZeroEpsilon)
        {
            position.Add(new XAttribute(axis, FormatNumber(value)));
        }
    }

    private static XElement? BuildRotation(Rotation rotation)
    {
        var hasYaw = Math.Abs(rotation.Yaw) >= ZeroEpsilon;
        var hasPitch = Math.Abs(rotation.Pitch) >= ZeroEpsilon;
        var hasRoll = Math.Abs(rotation.Roll) >= ZeroEpsilon;

        if (!hasYaw && !hasPitch && !hasRoll)
        {
            return null;
        }

        var element = new XElement("rotation");
        if (hasYaw)
        {
            element.Add(new XAttribute("yaw", FormatNumber(rotation.Yaw)));
        }

        if (hasPitch)
        {
            element.Add(new XAttribute("pitch", FormatNumber(rotation.Pitch)));
        }

        if (hasRoll)
        {
            element.Add(new XAttribute("roll", FormatNumber(rotation.Roll)));
        }

        return element;
    }

    private static XElement? BuildPatches(LayoutResult layout, IReadOnlyList<DlcInfo>? dlcs)
    {
        var extensions = layout.Modules
            .Select(m => m.Module.SourceExtension)
            .Where(e => !string.IsNullOrWhiteSpace(e)
                        && e!.StartsWith("ego_dlc", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extensions.Count == 0)
        {
            return null;
        }

        var byId = (dlcs ?? [])
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var patches = new XElement("patches");
        foreach (var ext in extensions)
        {
            var patch = new XElement("patch", new XAttribute("extension", ext!));
            if (byId.TryGetValue(ext!, out var info))
            {
                if (!string.IsNullOrWhiteSpace(info.Version))
                {
                    patch.Add(new XAttribute("version", info.Version!));
                }

                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    patch.Add(new XAttribute("name", info.Name!));
                }
            }

            patches.Add(patch);
        }

        return patches;
    }

    /// <summary>Lower-cases snap connection names to match the game's written plans (case-insensitive in-game).</summary>
    private static string NormalizeConnection(string connection) =>
        connection.ToLowerInvariant();

    private static string FormatNumber(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string GeneratePlanId() =>
        "player_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
