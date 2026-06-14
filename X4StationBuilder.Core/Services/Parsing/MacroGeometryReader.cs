using System.Globalization;
using System.Xml.Linq;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Reads per-module footprint and connection-point geometry from the macro/component XML.
/// </summary>
/// <remarks>
/// Each module macro (<c>index/macros.xml</c>) references a component (<c>&lt;component ref&gt;</c>)
/// resolved via <c>index/components.xml</c>. The component's <c>&lt;connections&gt;</c> hold the real
/// snap points (<c>tags</c> contains <c>snap</c>), ship docking bays (<c>dockingbay</c>) and docking
/// approach exclusion volumes (<c>exclusionzone</c>), each with a local <c>&lt;position&gt;</c> (cm)
/// and optional <c>&lt;quaternion&gt;</c>. Footprint half-extents are derived from the snap-point
/// distances. Only the macros actually used by scanned modules are read, so this stays cheap.
/// Reuses the same BOM/overlay/extension-path handling as <see cref="MacroWorkforceReader"/>.
/// </remarks>
public static class MacroGeometryReader
{
    private const string MacroIndex = "index/macros.xml";
    private const string ComponentIndex = "index/components.xml";

    /// <summary>
    /// Returns a map of macro name → <see cref="ModuleGeometry"/> for the requested
    /// <paramref name="macroNames"/>. Macros whose geometry cannot be resolved are omitted.
    /// </summary>
    public static Dictionary<string, ModuleGeometry> Read(X4FileSystem fs, IEnumerable<string> macroNames)
    {
        var result = new Dictionary<string, ModuleGeometry>(StringComparer.OrdinalIgnoreCase);

        var wanted = new HashSet<string>(macroNames, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return result;
        }

        var macroToPath = BuildIndex(fs, MacroIndex);
        var componentToPath = BuildIndex(fs, ComponentIndex);

        foreach (var macro in wanted)
        {
            if (!macroToPath.TryGetValue(macro, out var macroPath))
            {
                continue;
            }

            var componentRef = ReadComponentRef(fs, macroPath);
            if (componentRef is null || !componentToPath.TryGetValue(componentRef, out var compPath))
            {
                continue;
            }

            var geometry = ReadGeometry(fs, compPath);
            if (geometry is not null)
            {
                result[macro] = geometry;
            }
        }

        return result;
    }

    /// <summary>Builds name → file path (without extension) from a merged index file.</summary>
    private static Dictionary<string, string> BuildIndex(X4FileSystem fs, string indexPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in fs.GetAll(indexPath))
        {
            string xml;
            try
            {
                xml = CatDatReader.ReadText(entry);
            }
            catch (IOException)
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(StripBom(xml));
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var e in doc.Descendants("entry"))
            {
                var name = e.Attribute("name")?.Value;
                var value = e.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                {
                    map[name] = value; // later overlays win
                }
            }
        }

        return map;
    }

    /// <summary>Resolves the <c>&lt;component ref&gt;</c> for a module macro file.</summary>
    private static string? ReadComponentRef(X4FileSystem fs, string macroIndexValue)
    {
        var doc = LoadXml(fs, macroIndexValue);
        return doc?.Descendants("macro").FirstOrDefault()?
            .Element("component")?.Attribute("ref")?.Value;
    }

    /// <summary>Parses connection points and footprint from a component file.</summary>
    private static ModuleGeometry? ReadGeometry(X4FileSystem fs, string componentIndexValue)
    {
        var doc = LoadXml(fs, componentIndexValue);
        var component = doc?.Descendants("component").FirstOrDefault();
        var connectionsEl = component?.Element("connections");
        if (connectionsEl is null)
        {
            return null;
        }

        var connections = new List<ConnectionPoint>();
        foreach (var c in connectionsEl.Elements("connection"))
        {
            var name = c.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var tags = ParseTags(c.Attribute("tags")?.Value);
            var kind = ClassifyConnection(tags);

            // Only keep connection points the layout engine cares about; skipping the dozens of
            // part/turret/info connections keeps the persisted geometry small.
            if (kind == ConnectionKind.Other)
            {
                continue;
            }

            var offset = c.Element("offset");
            var position = ParsePosition(offset?.Element("position"));
            connections.Add(new ConnectionPoint
            {
                Name = name,
                Kind = kind,
                Tags = tags,
                Position = position,
                Direction = PrincipalDirection(position),
            });
        }

        if (connections.Count == 0)
        {
            return null;
        }

        return new ModuleGeometry
        {
            Connections = connections,
            HalfExtents = DeriveHalfExtents(connections),
        };
    }

    /// <summary>Footprint half-extents (cm) per axis from the snap-point distances.</summary>
    private static Vec3 DeriveHalfExtents(IReadOnlyList<ConnectionPoint> connections)
    {
        double x = 0, y = 0, z = 0;
        foreach (var c in connections)
        {
            if (c.Kind != ConnectionKind.Snap)
            {
                continue;
            }

            x = Math.Max(x, Math.Abs(c.Position.X));
            y = Math.Max(y, Math.Abs(c.Position.Y));
            z = Math.Max(z, Math.Abs(c.Position.Z));
        }

        return new Vec3(x, y, z);
    }

    /// <summary>Snaps a local position to its dominant principal axis as a unit direction.</summary>
    private static Vec3 PrincipalDirection(Vec3 p)
    {
        var ax = Math.Abs(p.X);
        var ay = Math.Abs(p.Y);
        var az = Math.Abs(p.Z);

        if (ax <= 1e-6 && ay <= 1e-6 && az <= 1e-6)
        {
            return Vec3.Zero;
        }

        if (ax >= ay && ax >= az)
        {
            return new Vec3(Math.Sign(p.X), 0, 0);
        }

        if (ay >= ax && ay >= az)
        {
            return new Vec3(0, Math.Sign(p.Y), 0);
        }

        return new Vec3(0, 0, Math.Sign(p.Z));
    }

    private static ConnectionKind ClassifyConnection(IReadOnlyList<string> tags)
    {
        if (tags.Contains("snap")) return ConnectionKind.Snap;
        if (tags.Contains("exclusionzone")) return ConnectionKind.ExclusionZone;
        if (tags.Contains("dockingbay")) return ConnectionKind.DockingBay;
        return ConnectionKind.Other;
    }

    private static IReadOnlyList<string> ParseTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Vec3 ParsePosition(XElement? position)
    {
        if (position is null)
        {
            return Vec3.Zero;
        }

        return new Vec3(
            ParseDouble(position.Attribute("x")?.Value),
            ParseDouble(position.Attribute("y")?.Value),
            ParseDouble(position.Attribute("z")?.Value));
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>Loads and parses the highest-precedence overlay of an index-referenced XML file.</summary>
    private static XDocument? LoadXml(X4FileSystem fs, string indexValue)
    {
        foreach (var candidate in CandidatePaths(indexValue))
        {
            var entries = fs.GetAll(candidate);
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                string xml;
                try
                {
                    xml = CatDatReader.ReadText(entries[i]);
                }
                catch (IOException)
                {
                    continue;
                }

                try
                {
                    return XDocument.Parse(StripBom(xml));
                }
                catch (System.Xml.XmlException)
                {
                    // try the next overlay
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Candidate virtual-filesystem paths for an index value. Extension archives store entries
    /// relative to the extension root, but the index records the full <c>extensions/&lt;id&gt;/…</c>
    /// path, so the prefix-stripped form is also tried.
    /// </summary>
    private static IEnumerable<string> CandidatePaths(string indexValue)
    {
        var path = NormalizeFilePath(indexValue);
        yield return path;

        if (path.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase))
        {
            var slash = path.IndexOf('/', "extensions/".Length);
            if (slash >= 0)
            {
                yield return path[(slash + 1)..];
            }
        }
    }

    private static string NormalizeFilePath(string indexValue)
    {
        var path = indexValue.Replace('\\', '/').TrimStart('/');
        return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path : path + ".xml";
    }

    private static string StripBom(string xml) =>
        xml.TrimStart('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
}
