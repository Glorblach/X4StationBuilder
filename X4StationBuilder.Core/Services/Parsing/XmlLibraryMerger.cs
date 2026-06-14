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
/// <c>&lt;diff&gt;</c> document containing <c>&lt;add&gt;</c> nodes whose children are new records.
/// This merger returns the base records plus every <c>&lt;add&gt;</c>-ed record (DLC content),
/// tagging each with the extension id it came from. <c>replace</c>/<c>remove</c> diff ops are
/// ignored for now (documented limitation).
/// </remarks>
public static class XmlLibraryMerger
{
    /// <summary>One merged record element plus the extension id that contributed it (null = base).</summary>
    public readonly record struct MergedElement(XElement Element, string? SourceExtension);

    /// <summary>
    /// Reads and merges every version of <paramref name="libraryPath"/> across the file system,
    /// returning the named top-level records (e.g. all <c>&lt;module&gt;</c> elements).
    /// </summary>
    public static List<MergedElement> Merge(X4FileSystem fs, string libraryPath, string recordElementName)
    {
        var results = new List<MergedElement>();

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

            var root = doc.Root;
            if (root is null)
            {
                continue;
            }

            var sourceExtension = entry.DatPath.Contains(
                $"{Path.DirectorySeparatorChar}extensions{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                ? ExtractExtensionId(entry.DatPath)
                : null;

            foreach (var record in ExtractRecords(root, recordElementName))
            {
                results.Add(new MergedElement(record, sourceExtension));
            }
        }

        return results;
    }

    private static IEnumerable<XElement> ExtractRecords(XElement root, string recordElementName)
    {
        if (root.Name.LocalName == "diff")
        {
            // Records added by an extension live under <add> nodes.
            foreach (var add in root.Elements("add"))
            {
                foreach (var record in add.Elements(recordElementName))
                {
                    yield return record;
                }
            }
        }
        else
        {
            foreach (var record in root.Elements(recordElementName))
            {
                yield return record;
            }
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
