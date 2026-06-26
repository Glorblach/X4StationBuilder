using System.Text.RegularExpressions;
using System.Xml.Linq;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Merges all versions of an X4 library XML file (base + extension overlays) into a single flat
/// list of top-level child elements.
/// </summary>
/// <remarks>
/// Base library files have a concrete root (e.g. <c>&lt;wares&gt;</c>, <c>&lt;modules&gt;</c>) whose
/// children are the records of interest. Extensions usually patch the same path with a
/// <c>&lt;diff&gt;</c> document containing <c>&lt;add&gt;</c> nodes. Two diff shapes are handled:
/// (1) <c>&lt;add&gt;</c> whose children are whole new records (DLC content), and
/// (2) <c>&lt;add sel="…/record[@id='X']"&gt;</c> whose children patch an existing record — used by
/// the DLCs to bolt extra <c>&lt;production&gt;</c> recipes (e.g. <c>method="terran"</c>) onto
/// base-game wares. <c>replace</c>/<c>remove</c> diff ops are ignored (documented limitation).
/// </remarks>
public static class XmlLibraryMerger
{
    /// <summary>One merged record element plus the extension id that contributed it (null = base).</summary>
    public readonly record struct MergedElement(XElement Element, string? SourceExtension);

    // Matches a diff selector whose final step targets a record by id, e.g. /wares/ware[@id='x'].
    private static readonly Regex RecordSelector =
        new(@"([A-Za-z_][\w.-]*)\[@id=['""]([^'""]+)['""]\]\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Reads and merges every version of <paramref name="libraryPath"/> across the file system,
    /// returning the named top-level records (e.g. all <c>&lt;module&gt;</c> elements).
    /// </summary>
    public static List<MergedElement> Merge(X4FileSystem fs, string libraryPath, string recordElementName)
    {
        var roots = new List<(XElement Root, string? Source)>();

        foreach (var entry in fs.GetAll(libraryPath))
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
                doc = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            if (doc.Root is null)
            {
                continue;
            }

            var sourceExtension = entry.DatPath.Contains(
                $"{Path.DirectorySeparatorChar}extensions{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                ? ExtractExtensionId(entry.DatPath)
                : null;

            roots.Add((doc.Root, sourceExtension));
        }

        return MergeRoots(roots, recordElementName);
    }

    /// <summary>
    /// Merges a sequence of already-parsed library roots (base + diff overlays) into the flat record
    /// list, applying both whole-record adds and record-patch overlays. Exposed for testing.
    /// </summary>
    public static List<MergedElement> MergeRoots(
        IEnumerable<(XElement Root, string? Source)> roots,
        string recordElementName)
    {
        var results = new List<MergedElement>();
        var byId = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var diffRoots = new List<XElement>();

        foreach (var (root, source) in roots)
        {
            if (root is null)
            {
                continue;
            }

            if (root.Name.LocalName == "diff")
            {
                diffRoots.Add(root);

                // Records added wholesale by an extension live under <add> nodes.
                foreach (var add in root.Elements("add"))
                {
                    foreach (var record in add.Elements(recordElementName))
                    {
                        results.Add(new MergedElement(record, source));
                        IndexById(byId, record);
                    }
                }
            }
            else
            {
                foreach (var record in root.Elements(recordElementName))
                {
                    results.Add(new MergedElement(record, source));
                    IndexById(byId, record);
                }
            }
        }

        ApplyRecordPatches(diffRoots, recordElementName, byId);

        return results;
    }

    /// <summary>
    /// Applies <c>&lt;add sel="…/record[@id='X']"&gt;</c> overlays by appending the overlay's child
    /// elements (e.g. extra <c>&lt;production&gt;</c> recipes) onto the existing record with that id.
    /// </summary>
    private static void ApplyRecordPatches(
        IEnumerable<XElement> diffRoots,
        string recordElementName,
        IReadOnlyDictionary<string, XElement> byId)
    {
        foreach (var diff in diffRoots)
        {
            foreach (var add in diff.Elements("add"))
            {
                var match = RecordSelector.Match(add.Attribute("sel")?.Value ?? string.Empty);
                if (!match.Success
                    || !string.Equals(match.Groups[1].Value, recordElementName, StringComparison.OrdinalIgnoreCase)
                    || !byId.TryGetValue(match.Groups[2].Value, out var target))
                {
                    continue;
                }

                foreach (var child in add.Elements())
                {
                    // Whole records are already emitted in pass 1; only graft sub-elements (recipes etc.).
                    if (string.Equals(child.Name.LocalName, recordElementName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    target.Add(new XElement(child));
                }
            }
        }
    }

    private static void IndexById(Dictionary<string, XElement> byId, XElement record)
    {
        var id = record.Attribute("id")?.Value;
        if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id))
        {
            byId[id] = record;
        }
    }

    private static string? ExtractExtensionId(string datPath)
    {
        var parts = datPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("extensions", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }

        return null;
    }
}
