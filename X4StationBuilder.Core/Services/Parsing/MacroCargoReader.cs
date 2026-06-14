using System.Xml.Linq;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Reads the per-module cargo capacity (m³) from storage module macro files.
/// </summary>
/// <remarks>
/// Cargo capacity is not present in <c>libraries/modules.xml</c>; it lives in each storage module's
/// macro file under <c>&lt;properties&gt;&lt;cargo max="…"/&gt;</c>. Macro file paths are resolved via
/// <c>index/macros.xml</c>. Only the macros actually used by scanned storage modules are read, so this
/// stays cheap.
/// </remarks>
public static class MacroCargoReader
{
    private const string MacroIndex = "index/macros.xml";

    /// <summary>
    /// Returns a map of macro name → cargo capacity (m³) for the requested <paramref name="macroNames"/>.
    /// Macros with no cargo (or unreadable files) are omitted.
    /// </summary>
    public static Dictionary<string, int> Read(X4FileSystem fs, IEnumerable<string> macroNames)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var wanted = new HashSet<string>(macroNames, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return result;
        }

        var macroToPath = BuildMacroPathIndex(fs);

        foreach (var macro in wanted)
        {
            if (!macroToPath.TryGetValue(macro, out var path))
            {
                continue;
            }

            var capacity = ReadCargo(fs, path);
            if (capacity > 0)
            {
                result[macro] = capacity;
            }
        }

        return result;
    }

    /// <summary>Builds macro name → file path (without extension) from the merged macro index.</summary>
    private static Dictionary<string, string> BuildMacroPathIndex(X4FileSystem fs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in fs.GetAll(MacroIndex))
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

    /// <summary>Reads the cargo capacity (<c>cargo/@max</c>) from a macro file.</summary>
    private static int ReadCargo(X4FileSystem fs, string indexValue)
    {
        foreach (var candidate in CandidatePaths(indexValue))
        {
            var entries = fs.GetAll(candidate);
            if (entries.Count == 0)
            {
                continue;
            }

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

                XDocument doc;
                try
                {
                    doc = XDocument.Parse(StripBom(xml));
                }
                catch (System.Xml.XmlException)
                {
                    continue;
                }

                var cargo = doc.Descendants("cargo").FirstOrDefault();
                if (cargo is null)
                {
                    continue;
                }

                if (int.TryParse(cargo.Attribute("max")?.Value, out var capacity))
                {
                    return capacity;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Candidate virtual-filesystem paths for a macro index value. Extension archives store entries
    /// relative to the extension root, but the macro index records the full
    /// <c>extensions/&lt;id&gt;/…</c> path, so the prefix-stripped form is also tried.
    /// </summary>
    private static IEnumerable<string> CandidatePaths(string indexValue)
    {
        var path = NormalizeMacroFilePath(indexValue);
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

    private static string NormalizeMacroFilePath(string indexValue)
    {
        var path = indexValue.Replace('\\', '/').TrimStart('/');
        return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path : path + ".xml";
    }

    /// <summary>Some X4 XML files carry a UTF-8 BOM/leading whitespace that <c>XDocument.Parse</c> rejects.</summary>
    private static string StripBom(string xml) =>
        xml.TrimStart('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
}
