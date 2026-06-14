using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace X4StationBuilder.Core.Services.Parsing;

/// <summary>
/// Resolves X4 localization references of the form <c>{page,id}</c> using one or more language
/// <c>t/*.xml</c> files.
/// </summary>
/// <remarks>
/// A language file has the shape <c>&lt;language&gt;&lt;page id="P"&gt;&lt;t id="N"&gt;text&lt;/t&gt;…&lt;/page&gt;…&lt;/language&gt;</c>.
/// Texts can embed further <c>{page,id}</c> references and a leading parenthetical template such as
/// <c>(Falcon Vanguard){…}</c>; <see cref="Resolve"/> expands nested references and trims those.
/// </remarks>
public sealed partial class LocalizationTable
{
    private readonly Dictionary<(int Page, int Id), string> _entries = new();

    public int Count => _entries.Count;

    /// <summary>Merges all <c>&lt;page&gt;/&lt;t&gt;</c> entries from a language XML document.</summary>
    public void LoadXml(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        var root = doc.Root;
        if (root is null)
        {
            return;
        }

        foreach (var page in root.Descendants("page"))
        {
            if (!int.TryParse(page.Attribute("id")?.Value, out var pageId))
            {
                continue;
            }

            foreach (var t in page.Elements("t"))
            {
                if (int.TryParse(t.Attribute("id")?.Value, out var tId))
                {
                    _entries[(pageId, tId)] = t.Value;
                }
            }
        }
    }

    /// <summary>Raw text for a (page, id) pair, or null.</summary>
    public string? GetRaw(int page, int id) =>
        _entries.TryGetValue((page, id), out var value) ? value : null;

    /// <summary>
    /// Resolves a localized name from a <c>{page,id}</c> reference (or a bare reference string),
    /// expanding nested references and stripping comment/template noise. Returns null if unresolved.
    /// </summary>
    public string? Resolve(string? reference, int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(reference) || depth > 8)
        {
            return null;
        }

        var match = ReferenceRegex().Match(reference);
        if (!match.Success)
        {
            return CleanUp(reference);
        }

        var page = int.Parse(match.Groups[1].Value);
        var id = int.Parse(match.Groups[2].Value);
        var raw = GetRaw(page, id);
        if (raw is null)
        {
            return null;
        }

        var expanded = ReferenceRegex().Replace(raw, m =>
        {
            var p = int.Parse(m.Groups[1].Value);
            var i = int.Parse(m.Groups[2].Value);
            return Resolve($"{{{p},{i}}}", depth + 1) ?? string.Empty;
        });

        return CleanUp(expanded);
    }

    private static string CleanUp(string text)
    {
        // Drop a leading parenthetical template like "(Falcon Vanguard)".
        text = LeadingParenRegex().Replace(text, string.Empty);
        // Collapse escaped newlines and whitespace runs.
        text = text.Replace("\\n", " ");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    [GeneratedRegex(@"\{\s*(\d+)\s*,\s*(\d+)\s*\}")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"^\([^)]*\)")]
    private static partial Regex LeadingParenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
