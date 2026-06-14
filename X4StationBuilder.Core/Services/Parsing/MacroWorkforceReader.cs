using System.Xml.Linq;
using X4StationBuilder.Core.Services.Archive;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Reads the per-module workforce capacity from the module macro component files.
/// </summary>
/// <remarks>
/// Workforce is not present in <c>libraries/modules.xml</c>; it lives in each module's macro file
/// under <c>&lt;properties&gt;&lt;workforce&gt;</c>:
/// production modules expose <c>max</c> (workers employed) and habitat modules expose
/// <c>capacity</c> (workers housed). Macro file paths are resolved via <c>index/macros.xml</c>.
/// Only the macros actually used by scanned modules are read, so this stays cheap.
/// </remarks>
public static class MacroWorkforceReader
{
    private const string MacroIndex = "index/macros.xml";

    /// <summary>
    /// Returns a map of macro name → workforce capacity for the requested <paramref name="macroNames"/>.
    /// Macros with no workforce (or unreadable files) are omitted.
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

            var capacity = ReadWorkforce(fs, path);
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

            // The index ships as a <diff>/<add> overlay; entries can appear at any depth.
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

    /// <summary>Reads the workforce capacity from a macro file (capacity preferred, else max).</summary>
    private static int ReadWorkforce(X4FileSystem fs, string indexValue)
    {
        foreach (var candidate in CandidatePaths(indexValue))
        {
            var entries = fs.GetAll(candidate);
            if (entries.Count == 0)
            {
                continue;
            }

            // Highest-precedence overlay wins; scan from the top down.
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

                var workforce = doc.Descendants("workforce").FirstOrDefault();
                if (workforce is null)
                {
                    continue;
                }

                var raw = workforce.Attribute("capacity")?.Value ?? workforce.Attribute("max")?.Value;
                if (int.TryParse(raw, out var capacity))
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
